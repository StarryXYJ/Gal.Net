using GalNet.Editor.Abstraction.Changes;

namespace GalNet.Editor.History;

/// <summary>Accumulates domain changes inside the history gate and publishes only a completed transaction.</summary>
public sealed class AuthorChangeJournal
{
    private List<AuthorGroupChange>? _pending;
    private readonly List<AuthorEntryMove> _moves = [];
    private readonly List<Action> _rollback = [];
    public long Revision { get; private set; }
    public Exception? LastNotificationError { get; private set; }
    public event Action<AuthorChangeSet>? Committed;

    internal void Begin() { _pending = []; _moves.Clear(); _rollback.Clear(); }
    internal void OnRollback(Action rollback)
    {
        if (_pending is null) throw new InvalidOperationException("Rollback callbacks require an active author transaction.");
        _rollback.Add(rollback);
    }
    internal void Cancel()
    {
        _pending = null; _moves.Clear();
        var callbacks = _rollback.ToArray(); _rollback.Clear();
        List<Exception> failures = [];
        foreach (var callback in callbacks.Reverse()) try { callback(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new HistoryRepairRequiredException("Author input rollback failed.", new AggregateException(failures));
    }
    public void RecordMove(AuthorEntryMove move)
    {
        if (_pending is null) throw new InvalidOperationException("Author moves must belong to a history transaction.");
        _moves.Add(move);
    }

    public void Record(string groupId, int firstIndex, GroupChangeKind kind, IEnumerable<string> entryIds)
    {
        if (_pending is null) throw new InvalidOperationException("Author changes must belong to a history transaction.");
        _pending.Add(new(groupId, Math.Max(0, firstIndex), kind, Array.AsReadOnly(entryIds.Distinct(StringComparer.Ordinal).ToArray())));
    }

    internal AuthorChangeSet Complete(AuthorChangeCause cause, bool knownScope)
    {
        var groups = (_pending ?? throw new InvalidOperationException("No author transaction is active."))
            .GroupBy(change => change.GroupId, StringComparer.Ordinal).Select(group => new AuthorGroupChange(group.Key,
                group.Min(change => change.FirstIndex), group.Aggregate(GroupChangeKind.None, (kind, change) => kind | change.Kind),
                Array.AsReadOnly(group.SelectMany(change => change.EntryIds).Distinct(StringComparer.Ordinal).ToArray()))).ToArray();
        _pending = null;
        _rollback.Clear();
        return new(++Revision, cause, Array.AsReadOnly(groups), !knownScope || groups.Length == 0) { Moves = Array.AsReadOnly(_moves.ToArray()) };
    }

    internal void Publish(AuthorChangeSet change)
    {
        if (Committed is null) return;
        foreach (Action<AuthorChangeSet> observer in Committed.GetInvocationList())
            try { observer(change); }
            catch (Exception failure)
            {
                LastNotificationError = failure;
                Serilog.Log.Error(failure, "An author change observer failed at revision {Revision}", change.Revision);
            }
    }
}

/// <summary>Implemented only when an edit records all its author impacts, including composite children.</summary>
public interface IAuthorScopedEdit
{
    bool HasKnownAuthorScope => true;
}
