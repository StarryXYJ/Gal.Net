using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>Project history and file-draft coordination for structured localization edits.</summary>
public sealed class LocalizationAuthoringService(LocalizationDocumentStore store, IProjectContentSession content, EditorHistories histories)
{
    public async Task SetTextAsync(LocalizationTextTarget target, string text)
    {
        await content.CommitFileDraftsAsync(target.File.Id);
        var patch = store.PrepareText(target, text);
        if (patch.Before.Exists && patch.Before.Data.AsSpan().SequenceEqual(patch.After)) return;
        await ApplyAsync(store.Prepare(patch), patch.Before.Path);
    }
    public Task ApplyAsync(LocalizationEditOperation operation, string label, string historyKey = "History.TextFile") => histories.Content.ExecuteAsync(new Edit(operation, label, historyKey));
    public IAsyncUndoableEdit CreateEdit(LocalizationEditOperation operation, string label, string historyKey) => new Edit(operation, label, historyKey);
    private sealed record Edit(LocalizationEditOperation Operation, string Label, string HistoryKey) : IAsyncUndoableEdit
    {
        public string Description => "Edit localization " + Label;
        public EditorText DisplayText => new(HistoryKey, Label);
        public long RetainedBytes => Operation.RetainedBytes;
        public ValueTask UndoAsync() => new(Operation.ApplyAsync(false));
        public ValueTask RedoAsync() => new(Operation.ApplyAsync(true));
    }
}
