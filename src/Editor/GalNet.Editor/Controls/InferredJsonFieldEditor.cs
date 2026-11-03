using System.ComponentModel;
using System.Text.Json;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Projects safely recognizable untyped objects onto the existing structured field editor.</summary>
internal static class InferredJsonFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext parent)
    {
        if (parent.EffectiveValue is not { ValueKind: JsonValueKind.Object } value || Infer(value, 0) is not { } descriptor)
            return DefaultFieldEditors.CreateJson(parent);
        var field = new FieldDescriptor(parent.Field.Id, descriptor, parent.Field.IsRequired, labelKey: parent.Field.LabelKey);
        var binding = new ForwardBinding(parent);
        var child = new FieldEditorContext(field, binding, parent.Registry);
        var lease = parent.Registry!.Resolve(field)(child);
        return new FieldEditorLease(lease.Control, () => { lease.Dispose(); child.Dispose(); binding.Dispose(); });
    }

    private static ValueDescriptor? Infer(JsonElement value, int depth)
    {
        if (depth >= 8) return null;
        switch (value.ValueKind)
        {
            case JsonValueKind.String: return new(typeof(string));
            case JsonValueKind.True or JsonValueKind.False: return new(typeof(bool));
            case JsonValueKind.Number: return value.TryGetDecimal(out _) ? new(typeof(decimal)) : null;
            case JsonValueKind.Object:
            {
                var fields = new List<FieldDescriptor>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (fields.Count >= 200 || string.IsNullOrWhiteSpace(property.Name) || !names.Add(property.Name) || Infer(property.Value, depth + 1) is not { } member) return null;
                    fields.Add(new(property.Name, member, labelKey: property.Name));
                }
                return new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Object(fields));
            }
            default: return null;
        }
    }

    private sealed class ForwardBinding : IFieldBinding, IDisposable
    {
        private readonly FieldEditorContext _parent;
        public ForwardBinding(FieldEditorContext parent) { _parent = parent; parent.PropertyChanged += ChangedParent; }
        public FieldDraftState? Draft => _parent.DraftState;
        public bool IsEnabled => _parent.IsEnabled;
        public bool IsVisible => _parent.IsVisible;
        public bool CanReset => false;
        public object? Label => _parent.Label;
        public object? HostContext => _parent.HostContext;
        public IReadOnlyList<string>? PropertySegments => _parent.PropertySegments;
        public FieldValue Read() => _parent.Value;
        public event EventHandler? Changed;
        private void ChangedParent(object? sender, PropertyChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
        public async Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token) =>
            await _parent.CommitAsync(value, token) ? null : _parent.Issue ?? new("structure.edit", "Unable to update the value.");
        public void Dispose() { _parent.PropertyChanged -= ChangedParent; Changed = null; }
    }
}
