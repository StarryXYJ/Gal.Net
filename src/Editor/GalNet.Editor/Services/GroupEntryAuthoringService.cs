using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;

namespace GalNet.Editor.Services;

/// <summary>Project-owned batch entry intents. History never retains a Group page.</summary>
public sealed class GroupEntryAuthoringService(EditorGroupProjectionStore groups, IEditorDocumentService documents, IEntryCatalog catalog, EditorHistories histories)
{
    public IReadOnlyList<EntryEditorItemViewModel> Insert(string groupId, int index, int count, string? type)
    {
        var group = Group(groupId);
        type ??= GalNet.Contracts.Builtins.BuiltinEntryIds.Text;
        if (!catalog.TryGet(type, out _)) return [];
        index = Math.Clamp(index, 0, group.Entries.Count);
        var inserted = Enumerable.Range(0, Math.Clamp(count, 1, MaximumEntries)).Select(_ => new EntryEditorItemViewModel(new EditorEntryData
        {
            StableId = Guid.NewGuid().ToString("N"),
            Type = type,
            Parameters = EditorEntryData.ParseParameters(catalog, type, catalog.Create(type).Values)
        }, catalog)).ToArray();
        histories.Content.Execute(new PresenceEdit(this, groupId, inserted.Select((entry, offset) => new PositionedEntry(index + offset, entry)).ToArray(), true,
            new("History.AddEntries", inserted.Length)));
        return inserted;
    }
    private sealed record EntryClipboard(string Kind, int Version, List<EditorEntryData> Entries);
    private const string ClipboardKind = "GalNet.Entries";
    private const int MaximumEntries = 1000;
    private const int MaximumBytes = 16 * 1024 * 1024;

    private EditorGroupProjection Group(string id)
    {
        if (!documents.CurrentDocument.Nodes.Any(node => node.Id == id && node.Type == "Group"))
            throw new EditorOperationException("entry.groupMissing", "The Group no longer exists.");
        return groups.Get(id);
    }

    public string Copy(string groupId, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var entries = Group(groupId).Entries.Where(entry => selected.Contains(entry.StableId)).Select(entry => entry.Source).ToList();
        if (entries.Count == 0) return "";
        if (entries.Count > MaximumEntries) throw InvalidClipboard();
        var text = JsonSerializer.Serialize(new EntryClipboard(ClipboardKind, 1, entries));
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw InvalidClipboard();
        return text;
    }

    public IReadOnlyList<EntryEditorItemViewModel> Paste(string groupId, string text, string? anchorId)
    {
        var group = Group(groupId);
        var index = anchorId is null ? group.Entries.Count : group.Entries.ToList().FindIndex(entry => entry.StableId == anchorId) + 1;
        if (anchorId is not null && index == 0) throw new EditorOperationException("entry.anchorMissing", "The insertion target was removed.");
        EntryClipboard clip;
        try
        {
            if (text.Length > MaximumBytes || Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new JsonException();
            clip = JsonSerializer.Deserialize<EntryClipboard>(text) ?? throw new JsonException();
            if (clip.Kind != ClipboardKind || clip.Version != 1 || clip.Entries is not { Count: > 0 and <= MaximumEntries }
                || clip.Entries.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Type) || entry.Condition is null || entry.Parameters is null)) throw new JsonException();
            // Check the entire payload before creating any live projection. Keep incomplete drafts;
            // full semantic validation belongs to compile/check, not clipboard insertion.
            foreach (var entry in clip.Entries)
            {
                if (!catalog.TryGet(entry.Type, out var definition) || entry.Parameters.Keys.Any(key => !definition.Parameters.ContainsKey(key)))
                    throw new EditorOperationException("entry.clipboardSchema", "The clipboard entries are not supported by this project's contract.");
                entry.StableId = Guid.NewGuid().ToString("N");
            }
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { throw InvalidClipboard(); }
        var inserted = clip.Entries.Select(entry => new EntryEditorItemViewModel(entry, catalog)).ToArray();
        var positions = inserted.Select((entry, offset) => new PositionedEntry(index + offset, entry)).ToArray();
        histories.Content.Execute(new PresenceEdit(this, groupId, positions, true, new("History.PasteEntries", inserted.Length)));
        return inserted;
    }

    public void Delete(string groupId, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var entries = Group(groupId).Entries.Select((entry, index) => new PositionedEntry(index, entry)).Where(item => selected.Contains(item.Entry.StableId)).ToArray();
        if (entries.Length > 0) histories.Content.Execute(new PresenceEdit(this, groupId, entries, false, new("History.DeleteEntries", entries.Length)));
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

    private sealed record PositionedEntry(int Index, EntryEditorItemViewModel Entry);
    private sealed record PresenceEdit(GroupEntryAuthoringService Owner, string GroupId, PositionedEntry[] Entries, bool Insert, EditorText DisplayText) : IUndoableEdit
    {
        public string Description => Insert ? "Paste entries" : "Delete entries";
        public long RetainedBytes => Entries.Sum(item => (long)JsonSerializer.SerializeToUtf8Bytes(item.Entry.Source).Length);
        public void Redo() => Apply(Insert);
        public void Undo() => Apply(!Insert);
        private void Apply(bool present)
        {
            var entries = Owner.Group(GroupId).Entries;
            ApplyAtomically(entries, () =>
            {
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
            });
        }
    }

    private sealed record OrderEdit(GroupEntryAuthoringService Owner, string GroupId, string[] Before, string[] After) : IUndoableEdit
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
            ApplyAtomically(entries, () =>
            {
                for (var index = 0; index < order.Length; index++)
                    if (!ReferenceEquals(entries[index], map[order[index]])) entries.Move(entries.IndexOf(map[order[index]]), index);
            });
        }
    }

    private static void ApplyAtomically(ObservableCollection<EntryEditorItemViewModel> entries, Action apply)
    {
        var before = entries.ToArray();
        try { apply(); Renumber(entries); }
        catch (Exception failure)
        {
            try
            {
                // ObservableCollection mutates before notifying. A failing observer can
                // therefore leave even its current operation applied; reconcile the
                // actual collection rather than counting completed calls.
                var retained = before.ToHashSet();
                for (var index = entries.Count - 1; index >= 0; index--)
                    if (!retained.Contains(entries[index])) entries.RemoveAt(index);
                for (var index = 0; index < before.Length; index++)
                {
                    var current = entries.IndexOf(before[index]);
                    if (current < 0) entries.Insert(index, before[index]);
                    else if (current != index) entries.Move(current, index);
                }
                Renumber(entries);
            }
            catch (Exception compensation) { throw new HistoryRepairRequiredException("Entry edit compensation failed.", new AggregateException(failure, compensation)); }
            throw;
        }
    }

    private static void Renumber(IEnumerable<EntryEditorItemViewModel> entries)
    {
        var index = 0;
        foreach (var entry in entries) entry.Id = ++index;
    }
    private static EditorOperationException InvalidClipboard() => new("entry.clipboard", "The clipboard does not contain a supported GalNet entry selection (up to 1,000 entries / 16 MiB of text).");
}
