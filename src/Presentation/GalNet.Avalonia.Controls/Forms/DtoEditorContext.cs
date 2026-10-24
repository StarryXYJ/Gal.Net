using Avalonia.Styling;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

public enum FieldDisplayMode { All, Common }

/// <summary>One mounted DTO editor's lifetime. Custom panels and automatic fields share the same host bindings.</summary>
public sealed class DtoEditorContext : IDisposable
{
    private readonly EditorRegistry _registry;
    private readonly ControlTheme? _formTheme;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, ScopedBinding> _bindings = new(StringComparer.Ordinal);
    private readonly List<FieldEditorContext> _fields = [];
    private bool _disposed, _submitting;
    private readonly int _depth;
    internal DtoEditorContext(DtoEditorItem item, EditorRegistry registry, ControlTheme? formTheme, int depth = 0, FieldDisplayMode displayMode = FieldDisplayMode.All)
    { Item = item; _registry = registry; _formTheme = formTheme; _depth = depth; DisplayMode = displayMode; }
    public FieldDisplayMode DisplayMode { get; }
    public DtoEditorItem Item { get; }
    public CancellationToken CancellationToken => _lifetime.Token;
    public IReadOnlyDictionary<string, FieldValue> Read() => Item.Fields.ToDictionary(field => field.Id, field => GetBinding(field).Read(), StringComparer.Ordinal);
    private IFieldBinding GetBinding(FieldDescriptor field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_bindings.TryGetValue(field.Id, out var binding)) _bindings.Add(field.Id, binding = new(this, Item.Binding(field)));
        return binding;
    }
    public FieldEditorContext CreateField(string id)
    {
        var field = Item.Fields.Single(field => field.Id == id);
        var context = new FieldEditorContext(field, GetBinding(field), _registry, _depth); _fields.Add(context); return context;
    }
    public SchemaForm CreateDefaultForm(params string[] exclude)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (exclude.Any(id => !Item.Fields.Any(field => field.Id == id))) throw new ArgumentException("Unknown excluded field.", nameof(exclude));
        var form = new SchemaForm { Depth = _depth, DtoType = Item.DtoType, Schema = Item.Fields.Where(field => !exclude.Contains(field.Id, StringComparer.Ordinal) && (DisplayMode == FieldDisplayMode.All || field.Editor?.IsCommon == true)).ToArray(), Registry = _registry, Binding = GetBinding };
        if (_formTheme is not null) form.Theme = _formTheme;
        return form;
    }
    public async Task<ValueIssue?> CommitAsync(IReadOnlyDictionary<string, FieldValue> patch)
    {
        if (_disposed) return new("dto.obsolete", "DTO editor has been released.");
        if (_submitting) return new("dto.busy", "A DTO edit is being submitted.");
        var copy = patch.ToDictionary(pair => pair.Key, pair => new FieldValue(pair.Value.Json), StringComparer.Ordinal);
        foreach (var (id, value) in copy)
        {
            var field = Item.Fields.FirstOrDefault(field => field.Id == id);
            if (field is null) return new("dto.field", "Unknown field.", id);
            if (!GetBinding(field).IsEnabled || !GetBinding(field).IsVisible) return new("field.unavailable", "Field is currently unavailable.", id);
            if (value.Json is { } json && ValueValidation.Validate(field.Value, json) is { } issue) return issue;
            if (!value.IsProvided && field.IsRequired && field.DefaultValue is null) return new("field.required", "Field must be provided.", id);
        }
        if (copy.Count == 0) return null;
        if (Item.Commit is null && copy.Count > 1) return new("dto.atomic", "The host must supply an atomic multi-field commit.");
        _submitting = true;
        try
        {
            var result = Item.Commit is { } commit ? await commit(copy, _lifetime.Token)
                : await GetBinding(Item.Fields.Single(field => field.Id == copy.Single().Key)).CommitAsync(copy.Single().Value, _lifetime.Token);
            return _disposed ? new("dto.obsolete", "DTO editor has been released.") : result;
        }
        catch (OperationCanceledException) when (_disposed) { return new("dto.obsolete", "DTO editor has been released."); }
        finally { _submitting = false; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        var errors = new List<Exception>();
        void Release(Action release) { try { release(); } catch (Exception exception) { errors.Add(exception); } }
        Release(() => _lifetime.Cancel());
        foreach (var field in _fields) Release(field.Dispose);
        foreach (var binding in _bindings.Values) Release(binding.Dispose);
        _lifetime.Dispose();
        if (errors.Count > 0) throw new AggregateException(errors);
    }
    private sealed class ScopedBinding(DtoEditorContext owner, IFieldBinding source) : IFieldBinding, IFieldPreviewBinding, IDisposable
    {
        public FieldDraftState? Draft => source.Draft;
        private EventHandler? _changed;
        private bool _subscribed;
        public FieldValue Read() => source.Read();
        public bool IsEnabled => !owner._disposed && source.IsEnabled;
        public bool IsVisible => source.IsVisible;
        public bool CanReset => source.CanReset;
        public object? Label => source.Label;
        public object? HostContext => source.HostContext;
        public IReadOnlyList<FieldEditorOption>? Options => source.Options;
        public bool SupportsPreview => !owner._disposed && source is IFieldPreviewBinding { SupportsPreview: true };
        public ValueIssue? Preview(FieldValue value) => !IsEnabled ? new("dto.obsolete", "DTO editor is unavailable.") : source is IFieldPreviewBinding preview ? preview.Preview(value) : new("dto.preview", "Preview is unavailable.");
        public event EventHandler? Changed
        {
            add { ObjectDisposedException.ThrowIf(owner._disposed, owner); if (!_subscribed) { source.Changed += OnChanged; _subscribed = true; } _changed += value; }
            remove { _changed -= value; if (_changed is null) Unsubscribe(); }
        }
        private void OnChanged(object? sender, EventArgs args) => _changed?.Invoke(this, args);
        public async Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
        {
            if (!IsEnabled) return new("dto.obsolete", "DTO editor is unavailable.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner._lifetime.Token);
            return await source.CommitAsync(value, linked.Token);
        }
        private void Unsubscribe() { if (!_subscribed) return; _subscribed = false; source.Changed -= OnChanged; }
        public void Dispose() { Unsubscribe(); _changed = null; }
    }
}
