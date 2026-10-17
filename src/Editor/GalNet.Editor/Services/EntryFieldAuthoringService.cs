using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;

namespace GalNet.Editor.Services;

/// <summary>Entry field intents retain author values and stable IDs, never a page or control.</summary>
public sealed class EntryFieldAuthoringService(IEditorDocumentService documents, EditorHistories histories)
{
    public event Action<string>? Changed;
    internal bool IsCurrent(string groupId, EditorEntryData expected) => documents.IsLoaded
        && documents.CurrentDocument.Nodes.Any(node => node.Id == groupId && node.Type == "Group")
        && documents.Groups.TryGetValue(groupId, out var group) && group.Entries.Any(entry => ReferenceEquals(entry, expected));

    public void SetCondition(string groupId, EditorEntryData expected, string value) => Update(groupId, expected, expected.Type,
        new(expected.Type, value, Clone(expected.Parameters)), "Edit Condition");

    public void SetParameters(string groupId, EditorEntryData expected, string type, Dictionary<string, JsonElement> parameters) =>
        Update(groupId, expected, type, new(type, expected.Condition, Clone(parameters)), "Edit Parameters");

    public void SetType(string groupId, EditorEntryData expected, string type, Dictionary<string, JsonElement> parameters) =>
        Update(groupId, expected, expected.Type, new(type, expected.Condition, Clone(parameters)), "Edit Type");

    private void Update(string groupId, EditorEntryData expected, string expectedType, EntryValue after, string description)
    {
        var entry = Find(groupId, expected.StableId);
        if (!ReferenceEquals(entry, expected) || entry.Type != expectedType) throw Obsolete();
        var before = Capture(entry);
        if (!Equal(before, after)) histories.Content.Execute(new FieldEdit(this, groupId, entry.StableId, before, after, description));
    }

    private EditorEntryData Find(string groupId, string id)
    {
        if (!documents.IsLoaded || !documents.CurrentDocument.Nodes.Any(node => node.Id == groupId && node.Type == "Group")
            || !documents.Groups.TryGetValue(groupId, out var group)) throw Obsolete();
        return group.Entries.SingleOrDefault(entry => entry.StableId == id) ?? throw Obsolete();
    }

    private sealed record EntryValue(string Type, string Condition, Dictionary<string, JsonElement> Parameters);
    private static Dictionary<string, JsonElement> Clone(IReadOnlyDictionary<string, JsonElement> values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
    private static EntryValue Capture(EditorEntryData entry) => new(entry.Type, entry.Condition, Clone(entry.Parameters));
    private static bool Equal(EntryValue left, EntryValue right) => left.Type == right.Type && left.Condition == right.Condition
        && left.Parameters.Count == right.Parameters.Count && left.Parameters.All(pair => right.Parameters.TryGetValue(pair.Key, out var value) && JsonElement.DeepEquals(pair.Value, value));
    private static void Write(EditorEntryData entry, EntryValue value)
    { entry.Type = value.Type; entry.Condition = value.Condition; entry.Parameters = Clone(value.Parameters); }
    private static EditorOperationException Obsolete() => new("entry.obsolete", "The entry or its schema is no longer current.");

    private sealed record FieldEdit(EntryFieldAuthoringService Owner, string GroupId, string EntryId, EntryValue Before, EntryValue After, string Description) : IUndoableEdit
    {
        public EditorText DisplayText => new(Description switch
        {
            "Edit Type" => "History.EntryType",
            "Edit Condition" => "History.EntryCondition",
            _ => "History.EntryParameters"
        });
        public long RetainedBytes => JsonSerializer.SerializeToUtf8Bytes(Before).LongLength + JsonSerializer.SerializeToUtf8Bytes(After).LongLength;
        public void Undo() => Apply(After, Before);
        public void Redo() => Apply(Before, After);
        private void Apply(EntryValue expected, EntryValue value)
        {
            var entry = Owner.Find(GroupId, EntryId);
            if (!Equal(Capture(entry), expected)) throw new EditorOperationException("entry.changed", "The entry changed outside this edit.");
            try { Write(entry, value); Owner.Changed?.Invoke(GroupId); }
            catch (Exception failure)
            {
                try { Write(entry, expected); Owner.Changed?.Invoke(GroupId); }
                catch (Exception compensation) { throw new HistoryRepairRequiredException("Entry field compensation failed.", new AggregateException(failure, compensation)); }
                throw;
            }
        }
    }
}
