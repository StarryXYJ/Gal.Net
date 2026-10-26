using System.Text.Json;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public sealed class EntryCutOffer
{
    public string Text { get; }
    internal string Token { get; }
    internal string GroupId { get; }
    internal string[] Ids { get; }
    internal string Source { get; }
    internal object Session { get; }
    internal long SourceVersion { get; }
    internal object Document { get; }
    internal EntryCutOffer(object session, string groupId, string[] ids, string source, string token, string text, long sourceVersion, object document)
    { Session = session; GroupId = groupId; Ids = ids; Source = source; Token = token; Text = text; SourceVersion = sourceVersion; Document = document; }
}

public sealed partial class GroupEntryAuthoringService
{
    private readonly object _moveSession = new();
    private EntryCutOffer? _pendingMove;
    private bool _observingMoves;
    private bool _disposed;
    private readonly Dictionary<string, long> _moveVersions = new(StringComparer.Ordinal);
    public bool LastPasteCopiedMove { get; private set; }
    public event Action? PendingMoveChanged;
    public bool HasPendingMove => _pendingMove is not null;
    public bool IsPendingMove(string groupId, string id) => _pendingMove is { } cut && cut.GroupId == groupId && cut.Ids.Contains(id, StringComparer.Ordinal);
    public EntryCutOffer? PrepareCut(string groupId, IEnumerable<string> ids)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObserveMoves();
        _moveVersions.TryAdd(groupId, 0);
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var ordered = Group(groupId).Entries.Where(entry => selected.Contains(entry.StableId)).Select(entry => entry.StableId).ToArray();
        var source = Copy(groupId, ordered);
        if (source.Length == 0) return null;
        var token = Guid.NewGuid().ToString("N");
        var clip = JsonSerializer.Deserialize<EntryClipboard>(source)!;
        return new(_moveSession, groupId, ordered, source, token, JsonSerializer.Serialize(clip with { MoveToken = token }), _moveVersions[groupId], documents.CurrentDocument);
    }
    public void BeginCut(EntryCutOffer offer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(offer.Session, _moveSession) || !CutIsCurrent(offer)) throw new EditorOperationException("entry.cutChanged", "The source entries changed while writing the clipboard.");
        _pendingMove = offer; NotifyPendingMove();
    }
    private void ObserveMoves()
    {
        if (_observingMoves) return;
        histories.Content.AuthorChanges.Committed += CheckPendingMove;
        documents.DocumentChanged += InvalidateMoves;
        _observingMoves = true;
    }
    private bool CutIsCurrent(EntryCutOffer offer)
    {
        if (_disposed || !documents.IsLoaded || !ReferenceEquals(documents.CurrentDocument, offer.Document) || _moveVersions.GetValueOrDefault(offer.GroupId) != offer.SourceVersion) return false;
        try { return Copy(offer.GroupId, offer.Ids) == offer.Source; }
        catch (EditorOperationException) { return false; }
    }
    private void CheckPendingMove(AuthorChangeSet change)
    {
        foreach (var groupId in _moveVersions.Keys.ToArray())
            if (change.UnknownScope || change.Groups.Any(group => group.GroupId == groupId)) _moveVersions[groupId]++;
        if (_pendingMove is { } cut && !CutIsCurrent(cut)) CancelPendingMove();
    }
    private void InvalidateMoves()
    {
        foreach (var groupId in _moveVersions.Keys.ToArray()) _moveVersions[groupId]++;
        CancelPendingMove();
    }
    public void CancelPendingMove() { if (_pendingMove is null) return; _pendingMove = null; NotifyPendingMove(); }
    private void NotifyPendingMove()
    {
        if (PendingMoveChanged is null) return;
        foreach (Action observer in PendingMoveChanged.GetInvocationList())
            try { observer(); } catch (Exception failure) { Serilog.Log.Error(failure, "A pending entry move observer failed"); }
    }
    private sealed record PreparedMove(PreparedEntryEdit Edit, HashSet<string> TargetIds);
    private bool TryPrepareMove(string targetId, string text, string? anchorId, out PreparedMove? prepared)
    {
        prepared = null;
        if (_pendingMove is not { } cut) return false;
        if (text != cut.Text || !CutIsCurrent(cut)) { CancelPendingMove(); return false; }
        var source = Group(cut.GroupId); var target = Group(targetId);
        var boundary = anchorId is null ? target.Entries.Count : target.Entries.FindIndex(entry => entry.StableId == anchorId) + 1;
        if (anchorId is not null && boundary == 0) throw new EditorOperationException("entry.anchorMissing", "The insertion target was removed.");
        var moving = source.Entries.Where(entry => cut.Ids.Contains(entry.StableId, StringComparer.Ordinal)).ToArray();
        if (source == target)
        {
            if (anchorId is not null && cut.Ids.Contains(anchorId, StringComparer.Ordinal)) { CancelPendingMove(); return true; }
            var before = source.Entries.Select(entry => entry.StableId).ToArray();
            var after = before.Where(id => !cut.Ids.Contains(id, StringComparer.Ordinal)).ToList();
            var offset = before.Take(boundary).Count(id => !cut.Ids.Contains(id, StringComparer.Ordinal));
            after.InsertRange(offset, cut.Ids);
            if (before.SequenceEqual(after)) { CancelPendingMove(); return true; }
            var edit = new OrderEdit(this, source.Id, before, after.ToArray());
            prepared = new(new(documents, histories, edit, candidate =>
            {
                var entries = candidate.GroupEntries[source.Id].ToDictionary(entry => entry.StableId);
                candidate.GroupEntries[source.Id] = after.Select(id => entries[id]).ToList();
            }, precondition: () => ReferenceEquals(_pendingMove, cut) && CutIsCurrent(cut)), cut.Ids.ToHashSet(StringComparer.Ordinal));
            return true;
        }
        var usedIds = target.Entries.Select(entry => entry.StableId).ToHashSet(StringComparer.Ordinal);
        var copies = moving.Select(entry => JsonSerializer.Deserialize<EditorEntryData>(JsonSerializer.Serialize(entry))!).ToArray();
        foreach (var copy in copies)
        {
            while (!usedIds.Add(copy.StableId)) copy.StableId = Guid.NewGuid().ToString("N");
        }
        var transitions = moving.Zip(copies).Select(pair => new EntryAddressTransition(new(source.Id, pair.First.StableId), new(target.Id, pair.Second.StableId))).ToArray();
        var oldSource = source.Entries.ToArray(); var oldTarget = target.Entries.ToArray();
        var nextSource = oldSource.Except(moving).ToArray(); var nextTarget = oldTarget.ToList(); nextTarget.InsertRange(boundary, copies);
        IUndoableEdit operation = new TransferEdit(this, source.Id, target.Id, oldSource, oldTarget, nextSource, nextTarget.ToArray(), transitions);
        if (edits is not null) operation = new EntryDraftLifecycleEdit(edits, operation, transitions, histories.Content.AuthorChanges);
        var draftRevision = edits?.Revision;
        prepared = new(new(documents, histories, operation, candidate =>
        {
            candidate.GroupEntries[source.Id].RemoveAll(entry => cut.Ids.Contains(entry.StableId, StringComparer.Ordinal));
            candidate.GroupEntries[target.Id].InsertRange(boundary, copies);
        }, edits?.CountPending(transitions.Select(address => address.Before)) ?? 0,
            () => ReferenceEquals(_pendingMove, cut) && CutIsCurrent(cut) && edits?.Revision == draftRevision,
            transitions.ToDictionary(address => address.After, address => address.Before)), copies.Select(entry => entry.StableId).ToHashSet(StringComparer.Ordinal));
        return true;
    }
    private sealed record TransferEdit(GroupEntryAuthoringService Owner, string SourceId, string TargetId,
        EditorEntryData[] OldSource, EditorEntryData[] OldTarget, EditorEntryData[] NextSource, EditorEntryData[] NextTarget,
        EntryAddressTransition[] Addresses) : IUndoableEdit, IAuthorScopedEdit
    {
        public string Description => "Move entries between Groups";
        public EditorText DisplayText => new("History.MoveEntries");
        public long RetainedBytes => JsonSerializer.SerializeToUtf8Bytes(new[] { OldSource, OldTarget, NextSource, NextTarget }).LongLength;
        public void Redo() => Apply(true);
        public void Undo() => Apply(false);
        private void Apply(bool forward)
        {
            var source = Owner.Group(SourceId).Entries; var target = Owner.Group(TargetId).Entries;
            var expectedSource = forward ? OldSource : NextSource; var expectedTarget = forward ? OldTarget : NextTarget;
            if (!source.SequenceEqual(expectedSource) || !target.SequenceEqual(expectedTarget)) throw new EditorOperationException("entry.changed", "Group membership changed outside this move.");
            var firstSource = CommonPrefix(source, forward ? NextSource : OldSource); var firstTarget = CommonPrefix(target, forward ? NextTarget : OldTarget);
            source.Clear(); source.AddRange(forward ? NextSource : OldSource); target.Clear(); target.AddRange(forward ? NextTarget : OldTarget);
            Renumber(source); Renumber(target);
            Owner.Record(SourceId, firstSource, GroupChangeKind.Membership, Addresses.Select(address => address.Before.EntryId));
            Owner.Record(TargetId, firstTarget, GroupChangeKind.Membership, Addresses.Select(address => address.After.EntryId));
            foreach (var address in Addresses)
            {
                var from = forward ? address.Before : address.After; var to = forward ? address.After : address.Before;
                Owner.RecordMove(new(from.GroupId, from.EntryId, to.GroupId, to.EntryId));
            }
        }
        private static int CommonPrefix(IEnumerable<EditorEntryData> before, IEnumerable<EditorEntryData> after) => before.Zip(after).TakeWhile(pair => ReferenceEquals(pair.First, pair.Second)).Count();
    }
    private void RecordMove(AuthorEntryMove move) => histories.Content.AuthorChanges.RecordMove(move);
    public void Dispose()
    {
        _disposed = true; _pendingMove = null;
        if (_observingMoves) { histories.Content.AuthorChanges.Committed -= CheckPendingMove; documents.DocumentChanged -= InvalidateMoves; }
        _moveVersions.Clear();
        PendingMoveChanged = null;
    }
}
