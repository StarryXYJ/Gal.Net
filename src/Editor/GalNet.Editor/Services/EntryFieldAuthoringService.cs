using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Abstraction.Changes;

namespace GalNet.Editor.Services;

/// <summary>Entry field intents retain author values and stable IDs, never a page or control.</summary>
public sealed class EntryFieldAuthoringService(IEditorDocumentService documents, EditorHistories histories, CharacterAuthoringService? characters = null, GameTextAuthoringService? texts = null)
{
    internal AuthorChangeJournal Changes => histories.Content.AuthorChanges;
    internal bool IsCurrent(string groupId, EditorEntryData expected) => documents.IsLoaded
        && documents.CurrentDocument.Nodes.Any(node => node.Id == groupId && node.Type == "Group")
        && documents.Groups.TryGetValue(groupId, out var group) && group.Entries.Any(entry => ReferenceEquals(entry, expected));

    public void SetCondition(string groupId, EditorEntryData expected, string value) => Update(groupId, expected, expected.Type,
        new(expected.Type, value, Clone(expected.Parameters)), "Edit Condition");

    public void SetParameters(string groupId, EditorEntryData expected, string type, Dictionary<string, JsonElement> parameters) =>
        Update(groupId, expected, type, new(type, expected.Condition, Clone(parameters)), "Edit Parameters");

    public async Task SetParametersAsync(string groupId, EditorEntryData expected, string type, Dictionary<string, JsonElement> parameters, CancellationToken token)
    {
        if (!IsCurrent(groupId, expected) || expected.Type != type) throw Obsolete();
        if (characters is not null && await characters.CommitParametersAsync(groupId, expected, type, parameters, () => IsCurrent(groupId, expected), token)) return;
        if (texts is not null && await texts.CommitEntryAsync(groupId, expected, type, parameters, () => IsCurrent(groupId, expected), token)) return;
        token.ThrowIfCancellationRequested();
        SetParameters(groupId, expected, type, parameters);
    }

    public void SetType(string groupId, EditorEntryData expected, string type, Dictionary<string, JsonElement> parameters) =>
        Update(groupId, expected, expected.Type, new(type, expected.Condition, GalNet.Editor.Shared.Services.SceneAuthoringIdentities.NewEntry(type, Clone(parameters))), "Edit Type");

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
    private int IndexOf(string groupId, EditorEntryData entry) => documents.Groups[groupId].Entries.IndexOf(entry);

    private sealed record EntryValue(string Type, string Condition, Dictionary<string, JsonElement> Parameters);
    private static Dictionary<string, JsonElement> Clone(IReadOnlyDictionary<string, JsonElement> values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
    private static EntryValue Capture(EditorEntryData entry) => new(entry.Type, entry.Condition, Clone(entry.Parameters));
    private static bool Equal(EntryValue left, EntryValue right) => left.Type == right.Type && left.Condition == right.Condition
        && left.Parameters.Count == right.Parameters.Count && left.Parameters.All(pair => right.Parameters.TryGetValue(pair.Key, out var value) && JsonElement.DeepEquals(pair.Value, value));
    private static void Write(EditorEntryData entry, EntryValue value)
    { entry.Type = value.Type; entry.Condition = value.Condition; entry.Parameters = Clone(value.Parameters); }
    private static EditorOperationException Obsolete() => new("entry.obsolete", "The entry or its schema is no longer current.");

    private sealed record FieldEdit(EntryFieldAuthoringService Owner, string GroupId, string EntryId, EntryValue Before, EntryValue After, string Description) : IUndoableEdit, IAuthorScopedEdit
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
            Write(entry, value);
            var kind = (expected.Type != value.Type ? GroupChangeKind.EntryType : GroupChangeKind.None)
                | (expected.Condition != value.Condition ? GroupChangeKind.Condition : GroupChangeKind.None)
                | (!Equal(expected with { Type = value.Type, Condition = value.Condition }, value) ? GroupChangeKind.Parameters : GroupChangeKind.None);
            Owner.Changes.Record(GroupId, Owner.IndexOf(GroupId, entry), kind, [EntryId]);
        }
    }
}
