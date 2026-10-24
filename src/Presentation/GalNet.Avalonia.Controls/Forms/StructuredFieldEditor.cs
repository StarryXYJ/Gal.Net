using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static Type? ElementType(Type type) => type.IsArray && type.GetArrayRank() == 1 ? type.GetElementType()
        : type.IsGenericType && type.GetGenericTypeDefinition() is var definition && (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IEnumerable<>)) ? type.GetGenericArguments()[0] : null;
    internal static bool Supports(Type type) => ElementType(type) is not null || typeof(IFieldDto).IsAssignableFrom(type) && type.GetProperty("Schema", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) is not null;
    internal static FieldEditorLease Create(FieldEditorContext context)
    {
        var type = Nullable.GetUnderlyingType(context.Field.Value.ValueType) ?? context.Field.Value.ValueType;
        if (context.Field.Value.IsStringEncoded || type.GetCustomAttribute<JsonConverterAttribute>() is not null || context.Field.Value.JsonOptions.Converters.Any(converter => converter.CanConvert(type)))
            return DefaultFieldEditors.CreateJson(context);
        var editor = new StructuredFieldEditor(context); return new(editor, editor.Dispose);
    }
    private StructuredFieldEditor(FieldEditorContext context)
    {
        Context = context; _type = Nullable.GetUnderlyingType(context.Field.Value.ValueType) ?? context.Field.Value.ValueType; _element = ElementType(_type);
        _snapshot = Current; Header = _type.Name; IsEnabled = context.IsEnabled; IsVisible = context.IsVisible;
        Classes.Add("structured-field");
        context.PropertyChanged += Changed;
    }
    private JsonElement? Current => Context.Value.Json ?? Context.Field.DefaultValue;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Context is null || _disposed) return;
        if (change.Property == IsExpandedProperty) { if (IsExpanded) Build(); else Release(); }
        else if (change.Property == ExpandByDefaultProperty && ExpandByDefault)
        {
            try { if (_element is null && Members(Context.Field.Value, _type).Count <= 6) IsExpanded = true; }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or TargetInvocationException) { }
        }
        else if (change.Property == ObjectTextProperty || change.Property == ListTextProperty || change.Property == RepairTextProperty)
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
            || _element is not null && !(_elementCommit && Equal(_submitted, current))
            || _element is null && current is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) }) Build();
        else foreach (var binding in _bindings.ToArray()) binding.Refresh();
    }
    private void Build()
    {
        Release(); if (_disposed || !IsExpanded) return;
        var value = Current;
        var kind = _element is null ? JsonValueKind.Object : JsonValueKind.Array;
        if (_jsonMode || value is { ValueKind: not JsonValueKind.Null } provided && provided.ValueKind != kind || _element is not null && value is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 200)
        { BuildJson(); return; }
        try
        {
            if (_element is null) BuildObject(); else BuildArray();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or TargetInvocationException)
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
    private string HeaderText() => _element is null ? ObjectText ?? _type.Name : (ListText ?? _type.Name) + " · " + ArrayValues().Count;
    private sealed record Member(FieldDescriptor Field, string Wire);
    private static IReadOnlyList<Member> Members(ValueDescriptor descriptor, Type type)
    {
        var schema = (IReadOnlyList<FieldDescriptor>)type.GetProperty("Schema", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
        var properties = descriptor.JsonOptions.GetTypeInfo(type).Properties;
        return schema.Select(field =>
        {
            var member = type.GetProperties().Single(property => property.GetCustomAttribute<FieldAttribute>() is not null &&
                (property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name)) == field.Id);
            var wire = properties.Single(property => property.AttributeProvider is MemberInfo info && info.Name == member.Name).Name;
            var options = new JsonSerializerOptions(descriptor.JsonOptions);
            foreach (var converter in field.Value.JsonOptions.Converters.Reverse()) options.Converters.Insert(0, converter);
            var child = new FieldDescriptor(field.Id, new(field.Value.ValueType, field.Value.AllowNull, field.Value.Constraints, options), field.IsRequired, field.DefaultValue, field.Editor, field.LabelKey, field.DescriptionKey, field.Semantic);
            return new Member(child, wire);
        }).ToArray();
    }
    private void BuildObject()
    {
        SetHeader(HeaderText());
        var members = Members(Context.Field.Value, _type); var fields = members.Select(member => member.Field).ToArray();
        var map = members.ToDictionary(member => member.Field.Id, member => member.Wire, StringComparer.Ordinal);
        var bindings = fields.ToDictionary(field => field.Id, field => Bind(field, map[field.Id], null), StringComparer.Ordinal);
        var generation = _generation;
        var item = new DtoEditorItem(Context.Field.Id, _type, fields, field => bindings[field.Id], (patch, token) => CommitObject(patch.ToDictionary(pair => map[pair.Key], pair => pair.Value), generation, token));
        Content = new DtoEditorHost { Depth = Context.Depth + 1, Item = item, Registry = Context.Registry };
    }
    private void BuildArray()
    {
        var values = ArrayValues(); SetHeader(HeaderText());
        var panel = new StackPanel { Spacing = 6 };
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
                using var copy = Context.Registry?.CopyValue?.Invoke(new(_element!, jsonOptions: Context.Field.Value.JsonOptions), items[position]) ?? new FieldCopy(items[position]);
                items.Insert(position + 1, copy.Value);
                await Submit(JsonSerializer.SerializeToElement(items), generation, CancellationToken.None);
            }).IsEnabled = values.Count < 200;
            row.Children.Add(buttons);
            var descriptor = new FieldDescriptor("element", new(_element!, jsonOptions: Context.Field.Value.JsonOptions), isRequired: true);
            var binding = Bind(descriptor, null, position);
            row.Children.Add(new SchemaForm { ShowLabels = false, Depth = Context.Depth + 1, Schema = [descriptor], Registry = Context.Registry, Binding = _ => binding });
            panel.Children.Add(row);
        }
        AddButton(panel, "add", () => ChangeArray(generation, list => list.Add(InitialValue(new(_element!, jsonOptions: Context.Field.Value.JsonOptions))))).IsEnabled = values.Count < 200;
        Content = panel;
    }
    private Button AddButton(Panel panel, string kind, Func<Task> action)
    {
        var button = new Button { Name = kind }; _buttons.Add((button, kind));
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { Context.ReportIssue(new("structure.edit", error.Message)); } };
        panel.Children.Add(button); RefreshButtonText(); return button;
    }
    private void RefreshButtonText()
    { foreach (var (button, kind) in _buttons) button.Content = kind switch { "add" => AddText, "remove" => RemoveText, "up" => UpText, "copy" => CopyText, _ => DownText }; }
    private List<JsonElement> ArrayValues() => Current is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().Select(item => item.Clone()).ToList() : [];
    private static JsonElement InitialValue(ValueDescriptor descriptor, int depth = 0)
    {
        var type = Nullable.GetUnderlyingType(descriptor.ValueType) ?? descriptor.ValueType;
        if (depth >= 8) return JsonSerializer.SerializeToElement<object?>(null);
        if (ElementType(type) is not null) return JsonSerializer.SerializeToElement(Array.Empty<object>());
        if (typeof(IFieldDto).IsAssignableFrom(type))
            return JsonSerializer.SerializeToElement(Members(descriptor, type).Where(member => member.Field.DefaultValue is not null || member.Field.IsRequired)
                .ToDictionary(member => member.Wire, member => member.Field.DefaultValue ?? InitialValue(member.Field.Value, depth + 1)));
        return JsonSerializer.SerializeToElement(type == typeof(string) ? "" : type.IsEnum ? Enum.GetValues(type).GetValue(0) : type.IsValueType ? Activator.CreateInstance(type) : null, descriptor.ValueType, descriptor.JsonOptions);
    }
    private ChildBinding Bind(FieldDescriptor field, string? wire, int? index)
    { var binding = new ChildBinding(this, field, wire, index, _generation); _bindings.Add(binding); return binding; }
    private async Task<ValueIssue?> CommitObject(IReadOnlyDictionary<string, FieldValue> patch, int generation, CancellationToken token)
    {
        if (!Available(generation)) return Obsolete();
        var values = Current is { ValueKind: JsonValueKind.Object } json ? json.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal) : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in patch) { if (value.Json is { } provided) values[key] = provided; else values.Remove(key); }
        return await Submit(JsonSerializer.SerializeToElement(values), generation, token);
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
        public bool IsEnabled => owner.Available(generation);
        public FieldValue Read()
        {
            var value = owner.Current;
            if (wire is not null && value is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(wire, out var member)) return new(member);
            if (index is { } position && value is { ValueKind: JsonValueKind.Array } array && position < array.GetArrayLength()) return new(array[position]);
            return default;
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
        if (_disposed) return; _disposed = true; Context.PropertyChanged -= Changed; Release(); _version.Dispose();
    }
}
