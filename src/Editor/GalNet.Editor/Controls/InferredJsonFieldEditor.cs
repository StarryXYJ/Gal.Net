using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Layout;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Projects safely recognizable untyped objects onto the existing structured field editor.</summary>
internal static class InferredJsonFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext parent)
    {
        if (parent.EffectiveValue is not { ValueKind: JsonValueKind.Object or JsonValueKind.Array } value || Infer(value, 0) is not { } descriptor)
            return DefaultFieldEditors.CreateJson(parent);
        var host = new InferredHost(parent, descriptor);
        return new FieldEditorLease(host, host.Dispose);
    }

    private sealed class InferredHost : ContentControl, IDisposable
    {
        private readonly FieldEditorContext _parent;
        private ValueDescriptor? _shape;
        private ForwardBinding? _binding;
        private FieldEditorContext? _child;
        private FieldEditorLease? _lease;
        private bool _disposed, _refreshQueued;
        public InferredHost(FieldEditorContext parent, ValueDescriptor shape)
        {
            _parent = parent; HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _parent.PropertyChanged += ParentChanged; Build(shape);
        }
        private void ParentChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (_disposed || _refreshQueued) return;
            _refreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _refreshQueued = false;
                if (_disposed || _parent.HasDraft || _parent.IsSubmitting ||
                    this.GetVisualDescendants().OfType<SchemaForm>().Any(form => form.Contexts.Any(context => context.HasDraft))) return;
                var next = _parent.Value.Json is { ValueKind: JsonValueKind.Object or JsonValueKind.Array } value ? Infer(value, 0) : null;
                if (next is null ? _shape is null : _shape is not null && SameShape(_shape, next)) return;
                Build(next);
            });
        }
        private void Build(ValueDescriptor? shape)
        {
            Content = null; _lease?.Dispose(); _child?.Dispose(); _binding?.Dispose();
            _shape = shape; _lease = null; _child = null; _binding = null;
            if (shape is null) _lease = DefaultFieldEditors.CreateJson(_parent);
            else
            {
                var field = new FieldDescriptor(_parent.Field.Id, shape, _parent.Field.IsRequired, labelKey: _parent.Field.LabelKey);
                _binding = new ForwardBinding(_parent);
                _child = new FieldEditorContext(field, _binding, _parent.Registry);
                _lease = _parent.Registry!.Resolve(field)(_child);
                if (_lease.Control is StructuredFieldEditor editor) editor.ShowSourceEditor = true;
            }
            Content = _lease.Control;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _parent.PropertyChanged -= ParentChanged;
            Content = null; _lease?.Dispose(); _child?.Dispose(); _binding?.Dispose();
        }
    }

    private static ValueDescriptor? Infer(JsonElement value, int depth)
    {
        if (depth >= 8) return null;
        switch (value.ValueKind)
        {
            case JsonValueKind.String: return new(typeof(string));
            case JsonValueKind.True or JsonValueKind.False: return new(typeof(bool));
            case JsonValueKind.Number:
                return value.TryGetDecimal(out var number) && JsonElement.DeepEquals(value, JsonSerializer.SerializeToElement(number))
                    ? new(typeof(decimal)) : null;
            case JsonValueKind.Array:
            {
                if (value.GetArrayLength() is 0 or > 200) return null;
                ValueDescriptor? element = null;
                foreach (var item in value.EnumerateArray())
                {
                    var next = Infer(item, depth + 1);
                    if (next is null || element is not null && !SameShape(element, next)) return null;
                    element ??= next;
                }
                return new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Sequence(new("element", element!)));
            }
            case JsonValueKind.Object:
            {
                var fields = new List<FieldDescriptor>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (fields.Count >= 200 || string.IsNullOrWhiteSpace(property.Name) || !names.Add(property.Name) || Infer(property.Value, depth + 1) is not { } member) return null;
                    fields.Add(new(property.Name, member, labelKey: property.Name));
                }
                if (fields.Count == 0) return null;
                return new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Object(fields));
            }
            default: return null;
        }
    }

    private static bool SameShape(ValueDescriptor left, ValueDescriptor right)
    {
        if (left.ValueType != right.ValueType || left.Structure?.Kind != right.Structure?.Kind) return false;
        if (left.Structure?.Kind == ValueStructureKind.Sequence)
            return SameShape(left.Structure.Element!.Value, right.Structure!.Element!.Value);
        if (left.Structure?.Kind != ValueStructureKind.Object) return true;
        var fields = right.Structure!.Fields!.ToDictionary(field => field.Id, StringComparer.Ordinal);
        return left.Structure.Fields!.Count == fields.Count && left.Structure.Fields.All(field =>
            fields.TryGetValue(field.Id, out var match) && SameShape(field.Value, match.Value));
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
