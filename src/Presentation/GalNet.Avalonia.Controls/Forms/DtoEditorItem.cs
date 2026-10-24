using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Stable identity and borrowed host bindings. No DTO construction or author-data ownership.</summary>
public sealed class DtoEditorItem
{
    public DtoEditorItem(string id, Type? dtoType, IReadOnlyList<FieldDescriptor> fields, Func<FieldDescriptor, IFieldBinding> binding,
        Func<IReadOnlyDictionary<string, FieldValue>, CancellationToken, Task<ValueIssue?>>? commit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentNullException.ThrowIfNull(fields); ArgumentNullException.ThrowIfNull(binding);
        if (fields.Select(field => field.Id).Distinct(StringComparer.Ordinal).Count() != fields.Count) throw new ArgumentException("Field IDs must be unique.", nameof(fields));
        Id = id; DtoType = dtoType; Fields = Array.AsReadOnly(fields.ToArray()); Binding = binding; Commit = commit;
    }
    public string Id { get; }
    public Type? DtoType { get; }
    public IReadOnlyList<FieldDescriptor> Fields { get; }
    internal Func<FieldDescriptor, IFieldBinding> Binding { get; }
    internal Func<IReadOnlyDictionary<string, FieldValue>, CancellationToken, Task<ValueIssue?>>? Commit { get; }
}
