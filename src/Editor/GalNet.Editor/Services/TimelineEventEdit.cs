using System.Text.Json;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

/// <summary>A cancelable event form. Member commits only modify this draft; Apply writes one author edit.</summary>
public sealed class TimelineEventEdit : IDisposable
{
    private readonly TimelineEditSession _owner;
    private readonly Dictionary<string, JsonElement> _values;
    private readonly Dictionary<string, EventField> _fields;
    private bool _disposed;
    internal bool Independent { get; }
    internal TimelineDocument Document { get; }
    internal int Generation { get; }
    internal int Frame { get; }
    internal int Index { get; }
    internal Dictionary<string, FieldValue> Patch { get; } = new(StringComparer.Ordinal);
    public bool IsCurrent => !_disposed && _owner.IsEventEditCurrent(this);
    public DtoEditorItem Item { get; }
    internal TimelineEventEdit(TimelineEditSession owner, TimelineDocument document, int generation, int frame, int index, string type, Type? dtoType, IReadOnlyList<FieldDescriptor> fields, bool independent = false)
    {
        Independent = independent;
        _owner = owner; Document = document; Generation = generation; Frame = frame; Index = index;
        var raw = document.Events[index].Raw;
        var parameters = raw.EnumerateObject().FirstOrDefault(pair => pair.Name.Equals("parameters", StringComparison.OrdinalIgnoreCase)).Value;
        if (parameters.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) throw new InvalidDataException("Timeline.EditRejected");
        _values = parameters.ValueKind == JsonValueKind.Object ? parameters.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.Clone(), StringComparer.Ordinal) : new(StringComparer.Ordinal);
        _fields = fields.ToDictionary(field => field.Id, field => new EventField(this, field, type), StringComparer.Ordinal);
        Item = new("timeline-event-" + index, dtoType, fields, field => _fields[field.Id], CommitAsync);
    }
    public IFieldBinding Field(string name) => _fields[name];
    private Task<ValueIssue?> CommitAsync(IReadOnlyDictionary<string, FieldValue> patch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCurrent) return Task.FromResult<ValueIssue?>(new("timeline.obsolete", _owner.SceneText["Timeline.EditRejected"]));
        foreach (var (name, value) in patch)
            if (!_fields.TryGetValue(name, out var field) || !field.IsVisible) return Task.FromResult<ValueIssue?>(new("timeline.field", _owner.SceneText["Timeline.EditRejected"], name));
            else if (value.Json is { } json && ValueValidation.Validate(field.Descriptor.Value, json) is { } issue) return Task.FromResult<ValueIssue?>(issue);
        foreach (var (name, value) in patch) { if (value.Json is { } json) _values[name] = json.Clone(); else _values.Remove(name); Patch[name] = value; }
        foreach (var field in _fields.Values) field.Notify();
        return Task.FromResult<ValueIssue?>(null);
    }
    public bool Apply() => IsCurrent && _owner.ApplyEvent(this);
    public void Dispose() { if (_disposed) return; _disposed = true; foreach (var field in _fields.Values) field.Release(); }
    private sealed class EventField(TimelineEventEdit owner, FieldDescriptor descriptor, string type) : IFieldBinding
    {
        public FieldDescriptor Descriptor => descriptor;
        public bool IsEnabled => owner.IsCurrent;
        public bool IsVisible => !SceneFieldSemantics.IsOutput(type, descriptor.Id);
        public object? Label => SceneFieldSemantics.Label(type, descriptor.Id) ?? descriptor.LabelKey ?? "Entry.Parameter." + descriptor.Id;
        public IReadOnlyList<FieldEditorOption>? Options => owner.IsCurrent ? owner._owner.EventFieldOptions(owner.Index, type, descriptor.Id) : null;
        public FieldValue Read() => owner._values.TryGetValue(descriptor.Id, out var value) ? new(value) : default;
        public event EventHandler? Changed;
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token) => owner.CommitAsync(new Dictionary<string, FieldValue> { [descriptor.Id] = value }, token);
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public void Release() { Changed?.Invoke(this, EventArgs.Empty); Changed = null; }
    }
}
