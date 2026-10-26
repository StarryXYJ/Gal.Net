using System.Text;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Abstraction.Changes;

namespace GalNet.Editor.Services;

/// <summary>Project-owned batch entry intents. History never retains a Group page.</summary>
public sealed class GroupEntryAuthoringService(EditorGroupProjectionStore groups, IEditorDocumentService documents, IEntryCatalog catalog, EditorHistories histories, GameTextAuthoringService? texts = null)
{
    public IReadOnlyList<EntryEditorItemViewModel> Insert(string groupId, int index, int count, string? type)
    {
        var group = Group(groupId);
        type ??= GalNet.Contracts.Builtins.BuiltinEntryIds.Text;
        if (!catalog.TryGet(type, out _)) return [];
        index = Math.Clamp(index, 0, group.Entries.Count);
        var inserted = Enumerable.Range(0, Math.Clamp(count, 1, MaximumEntries)).Select(_ => new EditorEntryData
        {
            StableId = Guid.NewGuid().ToString("N"),
            Type = type,
            Parameters = SceneAuthoringIdentities.NewEntry(type, EditorEntryData.CreateParameters(catalog, type))
        }).ToArray();
        histories.Content.Execute(new PresenceEdit(this, groupId, inserted.Select((entry, offset) => new PositionedEntry(index + offset, entry)).ToArray(), true,
            new("History.AddEntries", inserted.Length)));
        return Resolve(groupId, inserted);
    }
    private sealed record EntryClipboard(string Kind, int Version, List<EditorEntryData> Entries, GameTextClipboard Texts);
    private const string ClipboardKind = "GalNet.Entries";
    private const int MaximumEntries = 1000;
    private const int MaximumBytes = 16 * 1024 * 1024;

    private EditorGroupDocument Group(string id)
    {
        if (!documents.CurrentDocument.Nodes.Any(node => node.Id == id && node.Type == "Group"))
            throw new EditorOperationException("entry.groupMissing", "The Group no longer exists.");
        if (!documents.Groups.TryGetValue(id, out var group)) throw new EditorOperationException("entry.groupMissing", "The Group source no longer exists.");
        return group;
    }

    private IReadOnlyList<EntryEditorItemViewModel> Resolve(string groupId, IEnumerable<EditorEntryData> entries)
    {
        var projection = groups.Get(groupId);
        var selected = entries.ToHashSet();
        return projection.Entries.Where(entry => selected.Contains(entry.Source)).ToArray();
    }
    private void Record(string groupId, int first, GroupChangeKind kind, IEnumerable<string> ids) => histories.Content.AuthorChanges.Record(groupId, first, kind, ids);

    public string Copy(string groupId, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var entries = Group(groupId).Entries.Where(entry => selected.Contains(entry.StableId)).ToList();
        if (entries.Count == 0) return "";
        if (entries.Count > MaximumEntries) throw InvalidClipboard();
        var text = JsonSerializer.Serialize(new EntryClipboard(ClipboardKind, 2, entries, texts?.CaptureCopies(entries) ?? new(new(), new())));
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw InvalidClipboard();
        return text;
    }

    public async Task<IReadOnlyList<EntryEditorItemViewModel>> PasteAsync(string groupId, string text, string? anchorId)
    {
        var group = Group(groupId);
        var index = anchorId is null ? group.Entries.Count : group.Entries.ToList().FindIndex(entry => entry.StableId == anchorId) + 1;
        if (anchorId is not null && index == 0) throw new EditorOperationException("entry.anchorMissing", "The insertion target was removed.");
        EntryClipboard clip;
        try
        {
            if (text.Length > MaximumBytes || Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new JsonException();
            clip = JsonSerializer.Deserialize<EntryClipboard>(text) ?? throw new JsonException();
            if (clip.Kind != ClipboardKind || clip.Version != 2 || clip.Entries is not { Count: > 0 and <= MaximumEntries }
                || clip.Entries.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Type) || entry.Condition is null || entry.Parameters is null)) throw new JsonException();
            GameTextAuthoringService.ValidateClipboard(clip.Texts);
            // Check the entire payload before creating any live projection. Keep incomplete drafts;
            // full semantic validation belongs to compile/check, not clipboard insertion.
            foreach (var entry in clip.Entries)
            {
                entry.StableId = Guid.NewGuid().ToString("N");
            }
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { throw InvalidClipboard(); }
        SceneAuthoringIdentities.RemapCopy(clip.Entries);
        var copy = texts is null ? null : await texts.PrepareCopiesAsync(clip.Texts, clip.Entries, null, () =>
        {
            _ = Group(groupId);
            if (anchorId is not null && !group.Entries.Any(entry => entry.StableId == anchorId)) throw new EditorOperationException("entry.anchorMissing", "The insertion target was removed.");
        });
        index = anchorId is null ? group.Entries.Count : group.Entries.ToList().FindIndex(entry => entry.StableId == anchorId) + 1;
        var inserted = clip.Entries.ToArray();
        var positions = inserted.Select((entry, offset) => new PositionedEntry(index + offset, entry)).ToArray();
        var presence = new PresenceEdit(this, groupId, positions, true, new("History.PasteEntries", inserted.Length));
        if (copy is null) histories.Content.Execute(presence);
        else await histories.Content.ExecuteAsync(new AsyncCompositeEdit("Paste entries", [copy, new AsyncEditAdapter(presence)], new("History.PasteEntries", inserted.Length)));
        return Resolve(groupId, inserted);
    }

    public void Delete(string groupId, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var entries = Group(groupId).Entries.Select((entry, index) => new PositionedEntry(index, entry)).Where(item => selected.Contains(item.Entry.StableId)).ToArray();
        if (entries.Length > 0) histories.Content.Execute(new PresenceEdit(this, groupId, entries, false, new("History.DeleteEntries", entries.Length)));
    }

    public PreparedEntryEdit? PrepareDelete(string groupId, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var entries = Group(groupId).Entries.Select((entry, index) => new PositionedEntry(index, entry)).Where(item => selected.Contains(item.Entry.StableId)).ToArray();
        return entries.Length == 0 ? null : new(documents, histories,
            new PresenceEdit(this, groupId, entries, false, new("History.DeleteEntries", entries.Length)),
            candidate => candidate.GroupEntries[groupId].RemoveAll(entry => selected.Contains(entry.StableId)));
    }

    public void Move(string groupId, IEnumerable<string> ids, string draggedId, int newIndex)
    {
        var entries = Group(groupId).Entries;
        var before = entries.Select(entry => entry.StableId).ToArray();
        var oldIndex = Array.IndexOf(before, draggedId);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= entries.Count || oldIndex == newIndex) return;
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        selected.Add(draggedId);
        var boundary = newIndex > oldIndex ? newIndex + 1 : newIndex;
        var offset = before.Take(boundary).Count(id => !selected.Contains(id));
        var after = before.Where(id => !selected.Contains(id)).ToList();
        after.InsertRange(offset, before.Where(selected.Contains));
        if (!before.SequenceEqual(after)) histories.Content.Execute(new OrderEdit(this, groupId, before, after.ToArray()));
    }

    private sealed record PositionedEntry(int Index, EditorEntryData Entry);
    private sealed record PresenceEdit(GroupEntryAuthoringService Owner, string GroupId, PositionedEntry[] Entries, bool Insert, EditorText DisplayText) : IUndoableEdit, IAuthorScopedEdit
    {
        public string Description => Insert ? "Paste entries" : "Delete entries";
        public long RetainedBytes => Entries.Sum(item => (long)JsonSerializer.SerializeToUtf8Bytes(item.Entry).Length);
        public void Redo() => Apply(Insert);
        public void Undo() => Apply(!Insert);
        private void Apply(bool present)
        {
            var entries = Owner.Group(GroupId).Entries;
            if (present)
            {
                if (Entries.Any(item => entries.Any(entry => entry.StableId == item.Entry.StableId))) throw new InvalidOperationException("Entry already exists.");
                foreach (var item in Entries) entries.Insert(Math.Min(item.Index, entries.Count), item.Entry);
            }
            else
            {
                if (Entries.Any(item => !entries.Contains(item.Entry))) throw new InvalidOperationException("Entry no longer exists.");
                foreach (var item in Entries.Reverse()) entries.Remove(item.Entry);
            }
            Renumber(entries);
            Owner.Record(GroupId, Entries.Min(item => item.Index), GroupChangeKind.Membership, Entries.Select(item => item.Entry.StableId));
        }
    }

    private sealed record OrderEdit(GroupEntryAuthoringService Owner, string GroupId, string[] Before, string[] After) : IUndoableEdit, IAuthorScopedEdit
    {
        public string Description => "Move entries";
        public EditorText DisplayText => new("History.MoveEntries");
        public long RetainedBytes => (Before.Length + After.Length) * 80L;
        public void Redo() => Apply(After);
        public void Undo() => Apply(Before);
        private void Apply(string[] order)
        {
            var entries = Owner.Group(GroupId).Entries;
            var map = entries.ToDictionary(entry => entry.StableId);
            if (map.Count != order.Length || order.Any(id => !map.ContainsKey(id))) throw new InvalidOperationException("Entry order has changed.");
            var first = entries.Select(entry => entry.StableId).Zip(order).TakeWhile(pair => pair.First == pair.Second).Count();
            entries.Clear(); entries.AddRange(order.Select(id => map[id])); Renumber(entries);
            Owner.Record(GroupId, first, GroupChangeKind.Order, order.Skip(first));
        }
    }

    private static void Renumber(IEnumerable<EditorEntryData> entries)
    {
        var index = 0;
        foreach (var entry in entries) entry.Id = ++index;
    }
    private static EditorOperationException InvalidClipboard() => new("entry.clipboard", "The clipboard does not contain a supported GalNet entry selection (up to 1,000 entries / 16 MiB of text).");
}
