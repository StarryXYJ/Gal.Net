using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>Project history and file-draft coordination for structured localization edits.</summary>
public sealed class LocalizationAuthoringService(LocalizationDocumentStore store, IProjectContentSession content,
    IEditorDocumentService documents, EditorHistories histories)
{
    public async Task SetTextAsync(LocalizationTextTarget target, string text)
    {
        await content.CommitFileDraftsAsync(target.File.Id);
        var patch = store.PrepareText(target, text);
        if (patch.Before.Exists && patch.Before.Data.AsSpan().SequenceEqual(patch.After)) return;
        await ApplyAsync(store.Prepare(patch), patch.Before.Path);
    }
    public Task ApplyAsync(LocalizationEditOperation operation, string label, string historyKey = "History.TextFile") =>
        histories.Content.ExecuteAsync(new Edit(operation, label, historyKey, documents, histories.Content.AuthorChanges));
    public IAsyncUndoableEdit CreateEdit(LocalizationEditOperation operation, string label, string historyKey) =>
        new Edit(operation, label, historyKey, documents, histories.Content.AuthorChanges);
    private sealed record Edit(LocalizationEditOperation Operation, string Label, string HistoryKey,
        IEditorDocumentService Documents, AuthorChangeJournal Changes) : IAsyncUndoableEdit, IAuthorScopedEdit
    {
        public bool HasKnownAuthorScope => Operation.EntryTargets.Count != 0 && !Operation.HasNonEntryTargets;
        public string Description => "Edit localization " + Label;
        public EditorText DisplayText => new(HistoryKey, Label);
        public long RetainedBytes => Operation.RetainedBytes;
        public ValueTask UndoAsync() => ApplyAsync(false);
        public ValueTask RedoAsync() => ApplyAsync(true);
        private async ValueTask ApplyAsync(bool forward)
        {
            await Operation.ApplyAsync(forward);
            if (!HasKnownAuthorScope) return;
            foreach (var group in Operation.EntryTargets.GroupBy(target => target.GroupId, StringComparer.Ordinal))
            {
                var entries = Documents.LoadedProject.GroupEntries[group.Key];
                var ids = group.Select(target => target.EntryId).Distinct(StringComparer.Ordinal).ToArray();
                var first = ids.Min(id => entries.FindIndex(entry => entry.StableId == id));
                if (first < 0) throw new InvalidOperationException($"Entry target is missing from group '{group.Key}'.");
                Changes.Record(group.Key, first, GroupChangeKind.Parameters, ids);
            }
        }
    }
}
