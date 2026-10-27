using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Lazy object/list editing over JSON snapshots. No author DTO is constructed or rewritten.</summary>
public sealed class StructuredFieldEditor : Expander, IDisposable
{
    protected override Type StyleKeyOverride => typeof(Expander);
    public static readonly StyledProperty<bool> ExpandByDefaultProperty = AvaloniaProperty.Register<StructuredFieldEditor, bool>(nameof(ExpandByDefault));
    public bool ExpandByDefault { get => GetValue(ExpandByDefaultProperty); set => SetValue(ExpandByDefaultProperty, value); }
    public static readonly StyledProperty<string?> ObjectTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string?>(nameof(ObjectText));
    public static readonly StyledProperty<string?> ListTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string?>(nameof(ListText));
    public static readonly StyledProperty<string> RepairTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string>(nameof(RepairText), "JSON");
    public string? ObjectText { get => GetValue(ObjectTextProperty); set => SetValue(ObjectTextProperty, value); }
    public string? ListText { get => GetValue(ListTextProperty); set => SetValue(ListTextProperty, value); }
    public string RepairText { get => GetValue(RepairTextProperty); set => SetValue(RepairTextProperty, value); }
    public static readonly StyledProperty<string> AddTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string>(nameof(AddText), "Add element");
    public static readonly StyledProperty<string> RemoveTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string>(nameof(RemoveText), "Remove element");
    public static readonly StyledProperty<string> UpTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string>(nameof(UpText), "Move up");
    public static readonly StyledProperty<string> DownTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string>(nameof(DownText), "Move down");
    public string AddText { get => GetValue(AddTextProperty); set => SetValue(AddTextProperty, value); }
    public string RemoveText { get => GetValue(RemoveTextProperty); set => SetValue(RemoveTextProperty, value); }
    public string UpText { get => GetValue(UpTextProperty); set => SetValue(UpTextProperty, value); }
    public string DownText { get => GetValue(DownTextProperty); set => SetValue(DownTextProperty, value); }
    public static readonly StyledProperty<string> CopyTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string>(nameof(CopyText), "Copy element");
    public string CopyText { get => GetValue(CopyTextProperty); set => SetValue(CopyTextProperty, value); }
    public FieldEditorContext Context { get; }
    private readonly Type _type;
    private readonly Type? _element;
    private readonly ResolvedValueStructure _shape;
    private readonly ValueStructureResolver _resolver = new();
    private bool IsMap => _shape.Kind == ValueStructureKind.Map;
    private readonly List<ChildBinding> _bindings = [];
    private readonly List<(Button Button, string Kind)> _buttons = [];
    private CancellationTokenSource _version = new();
    private FieldEditorLease? _fallback;
    private JsonElement? _snapshot;
    private JsonElement? _submitted;
    private bool _elementCommit;
    private bool _jsonMode;
    private Button? _jsonButton;
    private bool _disposed;
    private int _generation;
    internal static bool Supports(Type type) => Supports(new ValueDescriptor(type));
    internal static bool Supports(ValueDescriptor descriptor)
    {
        using var resolver = new ValueStructureResolver();
        return resolver.Resolve(new("structure", descriptor)).Kind is ValueStructureKind.Object or ValueStructureKind.Sequence or ValueStructureKind.Map;
    }
    internal static FieldEditorLease Create(FieldEditorContext context)
    {
        if (!Supports(context.Field.Value)) return DefaultFieldEditors.CreateJson(context);
        var editor = new StructuredFieldEditor(context); return new(editor, editor.Dispose);
    }
    private StructuredFieldEditor(FieldEditorContext context)
    {
        Context = context; DataContext = context;
        _type = Nullable.GetUnderlyingType(context.Field.Value.ValueType) ?? context.Field.Value.ValueType;
        using var document = new ValueDocument(context.Field, null, _resolver);
        _shape = document.Root.Structure;
        _element = _shape.Kind == ValueStructureKind.Sequence ? _shape.Element!.Value.ValueType : null;
        _snapshot = Current; Header = _type.Name; IsEnabled = context.IsEnabled; IsVisible = context.IsVisible;
        Classes.Add("structured-field");
        context.PropertyChanged += Changed;
    }
    private JsonElement? Current => Context.Value.Json ?? Context.Field.DefaultValue;
    public static readonly StyledProperty<string?> TitleTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string?>(nameof(TitleText));
    public string? TitleText { get => GetValue(TitleTextProperty); set => SetValue(TitleTextProperty, value); }
    public static readonly StyledProperty<string> KeyTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string>(nameof(KeyText), "Key");
    public string KeyText { get => GetValue(KeyTextProperty); set => SetValue(KeyTextProperty, value); }
    public static readonly StyledProperty<string> DuplicateKeyTextProperty = AvaloniaProperty.Register<StructuredFieldEditor, string>(nameof(DuplicateKeyText), "A key with this name already exists.");
    public string DuplicateKeyText { get => GetValue(DuplicateKeyTextProperty); set => SetValue(DuplicateKeyTextProperty, value); }
    public static readonly StyledProperty<double> ChildIndentProperty = AvaloniaProperty.Register<StructuredFieldEditor, double>(nameof(ChildIndent), 12);
    public double ChildIndent { get => GetValue(ChildIndentProperty); set => SetValue(ChildIndentProperty, value); }
    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<StructuredFieldEditor, double>(nameof(ItemSpacing), 6);
    public double ItemSpacing { get => GetValue(ItemSpacingProperty); set => SetValue(ItemSpacingProperty, value); }
    private JsonElement? DecodedCurrent { get { using var doc = new ValueDocument(Context.Field, Current, _resolver); return doc.Root.Content; } }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Context is null || _disposed) return;
        if (change.Property == IsExpandedProperty) { if (IsExpanded) Build(); else Release(); }
        else if (change.Property == ExpandByDefaultProperty && ExpandByDefault)
        {
            try { if (_shape.Kind == ValueStructureKind.Object && _shape.Members.Count <= 6) IsExpanded = true; }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { }
        }
        else if (change.Property == ObjectTextProperty || change.Property == ListTextProperty || change.Property == RepairTextProperty || change.Property == TitleTextProperty)
        {
            if (IsExpanded) SetHeader(_jsonMode || _fallback is not null ? "JSON" : HeaderText());
            else Header = HeaderText();
        }
        else if (change.Property == AddTextProperty || change.Property == RemoveTextProperty || change.Property == UpTextProperty || change.Property == DownTextProperty || change.Property == CopyTextProperty) RefreshButtonText();
    }
    private static bool Equal(JsonElement? left, JsonElement? right) => left is { } a ? right is { } b && JsonElement.DeepEquals(a, b) : right is null;
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed) return;
        if (_jsonButton is not null) _jsonButton.IsVisible = RepairText == "JSON" || _jsonMode || _fallback is not null || Context.Issue is not null;
        IsEnabled = Context.IsEnabled;
        IsVisible = Context.IsVisible;
        var current = Current;
        if (Equal(_snapshot, current)) { foreach (var binding in _bindings.ToArray()) binding.Refresh(); return; }
        _snapshot = current?.Clone();
        if (!IsExpanded) return;
        if (_fallback is not null || Context.IsSubmitting && !Equal(_submitted, current)
            || (_element is not null || IsMap) && !(_elementCommit && Equal(_submitted, current))
            || _element is null && DecodedCurrent is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) }) Build();
        else foreach (var binding in _bindings.ToArray()) binding.Refresh();
    }
    private void Build()
    {
        Release(); if (_disposed || !IsExpanded) return;
        var value = DecodedCurrent;
        var kind = _element is null ? JsonValueKind.Object : JsonValueKind.Array;
        if (_jsonMode || value is { ValueKind: not JsonValueKind.Null } provided && provided.ValueKind != kind || value is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 200 || IsMap && value is { ValueKind: JsonValueKind.Object } map && map.EnumerateObject().Take(201).Count() > 200)
        { BuildJson(); return; }
        try
        {
            using var snapshot = new ValueDocument(Context.Field, Current, _resolver);
            if (snapshot.Root.Issue is not null) { BuildJson(); return; }
            if (IsMap) BuildMap(); else if (_element is null) BuildObject(); else BuildArray();
            if (Content is Control child) child.Margin = new(ChildIndent, ItemSpacing, 0, 0);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        { Release(); BuildJson(); ToolTip.SetTip(this, error.InnerException?.Message ?? error.Message); }
    }
    private void BuildJson()
    { SetHeader("JSON · " + _type.Name); _fallback = DefaultFieldEditors.CreateJson(Context); Content = _fallback.Control; }
    private void SetHeader(string text)
    {
        var panel = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
        var button = new Button { Name = "JsonMode", Content = _jsonMode ? "≡" : RepairText == "JSON" ? "JSON" : "⋯", IsVisible = RepairText == "JSON" || _jsonMode || _fallback is not null || Context.Issue is not null };
        _jsonButton = button;
        ToolTip.SetTip(button, RepairText);
        button.Click += (_, _) =>
        {
            if (Context.HasDraft) return;
            _jsonMode = !_jsonMode; Build();
        };
        panel.Children.Add(button); Header = panel;
    }
    private string HeaderText() => (Context.LabelOverride?.ToString() ?? TitleText ?? Context.Label.ToString() ?? Context.Field.Id) + (_element is not null ? " · " + ArrayValues().Count : IsMap ? " · " + MapValues().Count : "");
    private sealed record Member(FieldDescriptor Field, string Wire);
    private static IReadOnlyList<Member> Members(ValueDescriptor descriptor, Type type)
    {
        using var document = new ValueDocument(new("structure", descriptor), null);
        return document.Root.Structure.Members.Select(member => new Member(member.Field, member.WireName)).ToArray();
    }
    private void BuildObject()
    {
        SetHeader(HeaderText());
        var members = Members(Context.Field.Value, _type); var fields = members.Select(member => member.Field).ToArray();
        var map = members.ToDictionary(member => member.Field.Id, member => member.Wire, StringComparer.Ordinal);
        var bindings = fields.ToDictionary(field => field.Id, field => Bind(field, map[field.Id], null), StringComparer.Ordinal);
        var generation = _generation;
        var item = new DtoEditorItem(Context.Field.Id, Context.Field.Value.Structure?.SchemaType ?? _type, fields, field => bindings[field.Id], (patch, token) => CommitObject(patch.ToDictionary(pair => map[pair.Key], pair => pair.Value), generation, token));
        Content = new DtoEditorHost { Depth = Context.Depth + 1, Item = item, Registry = Context.Registry };
    }
    private void BuildArray()
    {
        var values = ArrayValues(); SetHeader(HeaderText());
        var panel = new StackPanel { Spacing = ItemSpacing };
        var generation = _generation;
        for (var index = 0; index < values.Count; index++)
        {
            var position = index;
            var row = new StackPanel { Spacing = 4 };
            var buttons = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
            buttons.Children.Add(new TextBlock { Text = "[" + index + "]" });
            AddButton(buttons, "up", () => Move(position, -1, generation)).IsEnabled = index > 0;
            AddButton(buttons, "down", () => Move(position, 1, generation)).IsEnabled = index + 1 < values.Count;
            AddButton(buttons, "remove", () => ChangeArray(generation, list => list.RemoveAt(position)));
            AddButton(buttons, "copy", async () =>
            {
                if (!Available(generation)) return;
                var items = ArrayValues();
                var element = _shape.Element!;
                var wrapped = element.Semantic != FieldValueSemantic.None;
                var descriptor = wrapped ? new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Object([element])) : element.Value;
                var source = wrapped ? JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [element.Id] = items[position] }) : items[position];
                using var copy = Context.Registry?.CopyValue?.Invoke(descriptor, source) ?? new FieldCopy(source);
                items.Insert(position + 1, wrapped ? copy.Value.GetProperty(element.Id) : copy.Value);
                await Submit(JsonSerializer.SerializeToElement(items), generation, CancellationToken.None);
            }).IsEnabled = values.Count < 200;
            row.Children.Add(buttons);
            var descriptor = new FieldDescriptor(_shape.Element!.Id, _shape.Element.Value, isRequired: true, editor: _shape.Element.Editor, labelKey: _shape.Element.LabelKey, semantic: _shape.Element.Semantic);
            var binding = Bind(descriptor, null, position);
            row.Children.Add(new SchemaForm { ShowLabels = false, Depth = Context.Depth + 1, Schema = [descriptor], Registry = Context.Registry, Binding = _ => binding });
            panel.Children.Add(row);
        }
        AddButton(panel, "add", () => ChangeArray(generation, list => list.Add(InitialValue(_shape.Element!.Value)))).IsEnabled = values.Count < 200;
        Content = panel;
    }
    private Dictionary<string, JsonElement> MapValues() => DecodedCurrent is { ValueKind: JsonValueKind.Object } map
        ? map.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal) : new(StringComparer.Ordinal);
    private void BuildMap()
    {
        var values = MapValues(); SetHeader(HeaderText());
        var panel = new StackPanel { Spacing = ItemSpacing }; var generation = _generation;
        foreach (var pair in values)
        {
            var original = pair.Key;
            var row = new StackPanel { Spacing = ItemSpacing };
            var keys = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = ItemSpacing };
            var key = new TextBox { Name = "MapKey", Text = original, Watermark = KeyText, MinWidth = 120 };
            async Task Rename()
            {
                if (!Available(generation) || key.Text == original) return;
                var candidate = MapValues(); var renamed = key.Text ?? "";
                if (candidate.ContainsKey(renamed)) { Context.ReportIssue(new("structure.keyDuplicate", DuplicateKeyText)); return; }
                if (!candidate.ContainsKey(original)) return;
                var ordered = candidate.Select(item => new KeyValuePair<string, JsonElement>(item.Key == original ? renamed : item.Key, item.Value)).ToDictionary();
                await Submit(JsonSerializer.SerializeToElement(ordered), generation, CancellationToken.None);
            }
            key.KeyDown += async (_, args) =>
            {
                if (args.Key == global::Avalonia.Input.Key.Enter) { args.Handled = true; await Rename(); }
                else if (args.Key == global::Avalonia.Input.Key.Escape) { key.Text = original; args.Handled = true; }
            };
            key.LostFocus += async (_, _) => await Rename();
            keys.Children.Add(key);
            AddButton(keys, "remove", async () =>
            {
                if (!Available(generation)) return;
                var candidate = MapValues(); candidate.Remove(original);
                await Submit(JsonSerializer.SerializeToElement(candidate), generation, CancellationToken.None);
            });
            row.Children.Add(keys);
            var descriptor = _shape.Element!;
            row.Children.Add(new SchemaForm { Depth = Context.Depth + 1, ShowLabels = false, Schema = [descriptor], Registry = Context.Registry, Binding = _ => Bind(descriptor, original, null) });
            panel.Children.Add(row);
        }
        var additions = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = ItemSpacing };
        var input = new TextBox { Name = "MapNewKey", Watermark = KeyText, MinWidth = 120 }; additions.Children.Add(input);
        AddButton(additions, "add", async () =>
        {
            if (!Available(generation)) return;
            var candidate = MapValues(); var key = input.Text ?? "";
            if (candidate.ContainsKey(key)) { Context.ReportIssue(new("structure.keyDuplicate", DuplicateKeyText)); return; }
            candidate.Add(key, InitialValue(_shape.Element!.Value));
            await Submit(JsonSerializer.SerializeToElement(candidate), generation, CancellationToken.None);
        }).IsEnabled = values.Count < 200;
        panel.Children.Add(additions); Content = panel;
    }
    private Button AddButton(Panel panel, string kind, Func<Task> action)
    {
        var button = new Button { Name = kind }; _buttons.Add((button, kind));
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { Context.ReportIssue(new("structure.edit", error.Message)); } };
        panel.Children.Add(button); RefreshButtonText(); return button;
    }
    private void RefreshButtonText()
    { foreach (var (button, kind) in _buttons) button.Content = kind switch { "add" => AddText, "remove" => RemoveText, "up" => UpText, "copy" => CopyText, _ => DownText }; }
    private List<JsonElement> ArrayValues() => DecodedCurrent is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().Select(item => item.Clone()).ToList() : [];
    private static JsonElement InitialValue(ValueDescriptor descriptor, int depth = 0)
    {
        var type = Nullable.GetUnderlyingType(descriptor.ValueType) ?? descriptor.ValueType;
        if (depth >= 8) return JsonSerializer.SerializeToElement<object?>(null);
        using var document = new ValueDocument(new("initial", descriptor), null);
        var shape = document.Root.Structure;
        if (shape.Kind == ValueStructureKind.Sequence) return JsonSerializer.SerializeToElement(Array.Empty<object>());
        if (shape.Kind == ValueStructureKind.Map) return JsonSerializer.SerializeToElement(new Dictionary<string, object>());
        if (shape.Kind == ValueStructureKind.Object)
            return JsonSerializer.SerializeToElement(Members(descriptor, type).Where(member => member.Field.DefaultValue is not null || member.Field.IsRequired)
                .ToDictionary(member => member.Wire, member => member.Field.DefaultValue ?? InitialValue(member.Field.Value, depth + 1)));
        return JsonSerializer.SerializeToElement(type == typeof(string) ? "" : type.IsEnum ? Enum.GetValues(type).GetValue(0) : type.IsValueType ? Activator.CreateInstance(type) : null, descriptor.ValueType, descriptor.JsonOptions);
    }
    private ChildBinding Bind(FieldDescriptor field, string? wire, int? index)
    { var binding = new ChildBinding(this, field, wire, index, _generation); _bindings.Add(binding); return binding; }
    private async Task<ValueIssue?> CommitObject(IReadOnlyDictionary<string, FieldValue> patch, int generation, CancellationToken token)
    {
        if (!Available(generation)) return Obsolete();
        var candidate = Context.Value.Json;
        foreach (var (key, value) in patch)
        {
            using var document = new ValueDocument(Context.Field, candidate, _resolver);
            var changed = document.Patch([key], value.Json, token);
            if (changed.Issue is not null) return changed.Issue;
            candidate = changed.Value;
        }
        return candidate is { } result ? await Submit(result, generation, token, elementCommit: IsMap) : Obsolete();
    }

    private async Task ChangeArray(int generation, Action<List<JsonElement>> edit)
    {
        if (!Available(generation)) return;
        var values = ArrayValues(); edit(values); await Submit(JsonSerializer.SerializeToElement(values), generation, CancellationToken.None);
    }
    private Task Move(int index, int offset, int generation) => ChangeArray(generation, values => { var item = values[index]; values.RemoveAt(index); values.Insert(index + offset, item); });
    private bool Available(int generation) => !_disposed && IsExpanded && generation == _generation && Context.IsEnabled;
    private static ValueIssue Obsolete() => new("structure.obsolete", "The object or collection editor has changed.");
    private async Task<ValueIssue?> Submit(JsonElement json, int generation, CancellationToken token, bool elementCommit = false)
    {
        if (!Available(generation)) return Obsolete();
        if (_shape.AllowsJsonString && Context.Value.Json is { ValueKind: JsonValueKind.String } && json.ValueKind != JsonValueKind.String)
            json = JsonSerializer.SerializeToElement(json.GetRawText());
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _version.Token);
        _submitted = json.Clone(); _elementCommit = elementCommit;
        try { return await Context.CommitAsync(new(json), linked.Token) ? null : Context.Issue ?? Obsolete(); }
        finally { _submitted = null; _elementCommit = false; }
    }
    private sealed class ChildBinding(StructuredFieldEditor owner, FieldDescriptor field, string? wire, int? index, int generation) : IFieldBinding
    {
        public FieldDraftState? Draft => GetDraft();
        private FieldDraftState? GetDraft() => owner.Context.DraftState?.Child?.Invoke(field, wire ?? index!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public object? HostContext => owner.Context.HostContext;
        public IReadOnlyList<string>? PropertySegments => [.. owner.Context.PropertySegments ?? [], wire ?? index!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        public object? Label => index is { } position ? "#" + (position + 1) : owner.IsMap ? wire : null;
        public bool IsLabelLiteral => index is not null || owner.IsMap;
        public bool CanReset => owner.Context.AllowsChildReset;
        public bool IsVisible => owner.Context.IsVisible;
        public bool IsEnabled => owner.Available(generation);
        public FieldValue Read()
        {
            using var document = new ValueDocument(owner.Context.Field, owner.Current, owner._resolver);
            var property = document.FindProperty([wire ?? index!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            return new(property?.Value);
        }
        public event EventHandler? Changed;
        public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
        public void Release() => Changed = null;
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!IsEnabled) return Task.FromResult<ValueIssue?>(Obsolete());
            if (wire is not null) return owner.CommitObject(new Dictionary<string, FieldValue> { [wire] = value }, generation, token);
            if (value.Json is not { } element) return Task.FromResult<ValueIssue?>(new("structure.element", "Array elements cannot be omitted; remove the element instead.", field.Id));
            var values = owner.ArrayValues(); values[index!.Value] = element;
            return owner.Submit(JsonSerializer.SerializeToElement(values), generation, token, elementCommit: true);
        }
    }
    private void Release()
    {
        _generation++; _version.Cancel(); _version.Dispose(); _version = new();
        Content = null; _fallback?.Dispose(); _fallback = null;
        foreach (var binding in _bindings) binding.Release(); _bindings.Clear(); _buttons.Clear();
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Context.PropertyChanged -= Changed; Release(); _version.Dispose(); _resolver.Dispose();
    }
}
