using System.Text.Json;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public sealed partial class EntryEditSession
{
    private readonly IEditorDocumentService? _documents;
    private readonly IProjectService? _projects;
    private readonly EntryDraftArchiveLimits _limits;
    private readonly List<ArchivedEntryDraft> _archives = [];
    private readonly HashSet<string> _restoreRequested = new(StringComparer.Ordinal);
    private EntryDraftArchiveStore? _archiveStore;
    private bool _archiveLoaded;
    public long Revision { get; private set; }
    public Exception? ArchiveError { get; private set; }
    public IReadOnlyList<ArchivedEntryDraft> Archives { get { if (!_disposed) LoadArchive(); return _archives.AsReadOnly(); } }
    public int CountPending(IEnumerable<SceneAuthorAddress> addresses)
    {
        var selected = addresses.ToHashSet();
        return Pending.Count(draft => selected.Contains(Address(draft)));
    }
    private static SceneAuthorAddress Address(Draft draft) => new(draft.Owner.AuthorGroupId!, draft.Owner.StableId);
    private void LoadArchive()
    {
        if (_archiveLoaded) return;
        if (_projects?.Current is not { } project) { if (_projects is null) _archiveLoaded = true; return; }
        _archiveLoaded = true;
        try { _archiveStore = new(project.RootPath, _limits); _archives.AddRange(_archiveStore.Read()); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { ArchiveError = failure; }
    }
    private void WriteArchive(IReadOnlyList<ArchivedEntryDraft> records)
    {
        LoadArchive();
        if (ArchiveError is not null) throw new EditorOperationException("entry.archiveUnavailable", "Input recovery is unavailable. Preserve and repair .galnet/editor/input-archive.json first.");
        var bytes = EntryDraftArchiveStore.Encode(records, _limits);
        _archiveStore?.Write(bytes);
    }
    internal void ApplyAuthorEdit(IReadOnlyList<EntryAddressTransition> addresses, Action apply, AuthorChangeJournal? journal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LoadArchive();
        var moves = addresses.ToDictionary(address => address.Before, address => address.After);
        var affected = _drafts.Where(pair => moves.ContainsKey(Address(pair.Value))).ToArray();
        var added = affected.Select(pair => pair.Value).Where(draft => draft.State.IsDirty || !draft.State.CanCommit).Select(draft =>
            new ArchivedEntryDraft(Guid.NewGuid().ToString("N"), moves[Address(draft)], draft.Type, draft.Root.Id, draft.Path.ToArray(),
                draft.Field.Value.ValueType.FullName ?? draft.Field.Value.ValueType.Name, draft.State.Baseline ?? Baseline(draft), draft.State.Text, draft.State.JsonText, draft.State.Issue, draft.State.IsConflicted)).ToArray();
        var before = _archives.ToArray();
        var after = before.Select(record => moves.TryGetValue(record.Owner, out var target) ? record with { Owner = target } : record).Concat(added).ToArray();
        var changed = added.Length != 0 || !before.SequenceEqual(after);
        // Input recovery is published first. If the domain step fails, restore the previous recovery file.
        // A failed recovery write cannot destroy author content or detach its live inputs.
        if (changed) WriteArchive(after);
        try { apply(); }
        catch (Exception failure)
        {
            if (changed)
                try { WriteArchive(before); }
                catch (Exception rollback) { throw new HistoryRepairRequiredException("Entry input recovery rollback failed.", new AggregateException(failure, rollback)); }
            throw;
        }
        if (changed) { _archives.Clear(); _archives.AddRange(after); }
        List<Action> restoreDrafts = [];
        foreach (var (key, draft) in affected)
        {
            var commit = draft.State.Commit; var child = draft.State.Child;
            draft.State.Changed -= draft.OnChanged;
            draft.State.Child = null;
            draft.State.Commit = () => Task.FromResult<GalNet.Core.Parameters.ValueIssue?>(Error("Conflict"));
            var blocked = draft.State.RegisterGuard(() => false);
            _drafts.Remove(key);
            restoreDrafts.Add(() =>
            {
                blocked.Dispose(); draft.State.Commit = commit; draft.State.Child = child;
                _drafts[key] = draft; draft.State.Changed += draft.OnChanged;
            });
        }
        if (journal?.IsRecording == true) journal.OnRollback(() =>
        {
            if (changed) WriteArchive(before);
            _archives.Clear(); _archives.AddRange(before);
            foreach (var restore in restoreDrafts) restore();
            Revision++;
        });
        Revision++;
        // Content/Problems notification follows the completed history commit, never this intermediate step.
    }
    private string? Source(ArchivedEntryDraft record)
    {
        if (_documents?.IsLoaded != true || !_documents.CurrentDocument.Nodes.Any(node => node.Id == record.Owner.GroupId && node.Type == "Group")
            || !_documents.Groups.TryGetValue(record.Owner.GroupId, out var group)
            || group.Entries.FirstOrDefault(entry => entry.StableId == record.Owner.EntryId) is not { } entry || entry.Type != record.Type) return null;
        var value = entry.Parameters.GetValueOrDefault(record.RootId);
        // Localized text drafts include the source text in their captured baseline.
        return value.GetRawTextSafe() + (record.Baseline.Contains('\n') && value.ValueKind == JsonValueKind.String ? "\n" + _texts.Read(value.GetString()!) : "");
    }
    private string Baseline(Draft draft)
    {
        var value = draft.Owner.Source.Parameters.GetValueOrDefault(draft.Root.Id);
        return value.GetRawTextSafe() + (draft.Root.Semantic == GalNet.Core.Parameters.FieldValueSemantic.LocalizedText && value.ValueKind == JsonValueKind.String ? "\n" + _texts.Read(value.GetString()!) : "");
    }
    public bool CanRestore(ArchivedEntryDraft record) => !_disposed && _archives.Contains(record) && !record.Conflicted && Source(record) == record.Baseline
        && !_drafts.Values.Any(draft => Address(draft) == record.Owner && draft.Root.Id == record.RootId && draft.Path.SequenceEqual(record.Path) && (draft.State.IsDirty || !draft.State.CanCommit));
    public bool IsRestoreRequested(ArchivedEntryDraft record) => _restoreRequested.Contains(record.Id);
    public void RequestRestore(ArchivedEntryDraft record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanRestore(record)) throw new EditorOperationException("entry.archiveChanged", "The archived input no longer matches its author target. Copy its text before discarding it.");
        _restoreRequested.Add(record.Id);
        foreach (var draft in _drafts.Values.ToArray()) TryRestore(draft);
        NotifyChanged();
    }
    private void TryRestore(Draft draft)
    {
        LoadArchive();
        var record = _archives.FirstOrDefault(record => _restoreRequested.Contains(record.Id) && record.Owner == Address(draft)
            && record.Type == draft.Type && record.RootId == draft.Root.Id && record.Path.SequenceEqual(draft.Path)
            && record.FieldType == (draft.Field.Value.ValueType.FullName ?? draft.Field.Value.ValueType.Name));
        if (record is null || !CanRestore(record) || !draft.Owner.IsCurrent || draft.Owner.DraftIncarnation != draft.Incarnation) return;
        draft.State.Update(record.Text, true, record.Issue, record.JsonText);
        RemoveArchive(record);
    }
    public void RemoveArchive(ArchivedEntryDraft record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_archives.Contains(record)) return;
        var after = _archives.Where(item => item.Id != record.Id).ToArray();
        WriteArchive(after);
        _archives.Remove(record); _restoreRequested.Remove(record.Id); Revision++; NotifyChanged();
    }
}
