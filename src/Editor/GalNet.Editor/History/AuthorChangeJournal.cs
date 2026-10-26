using GalNet.Editor.Abstraction.Changes;

namespace GalNet.Editor.History;

/// <summary>Accumulates domain changes inside the history gate and publishes only a completed transaction.</summary>
public sealed class AuthorChangeJournal
{
    private List<AuthorGroupChange>? _pending;
    public long Revision { get; private set; }
    public Exception? LastNotificationError { get; private set; }
    public event Action<AuthorChangeSet>? Committed;

    internal void Begin() => _pending = [];
    internal void Cancel() => _pending = null;

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
        return new(++Revision, cause, Array.AsReadOnly(groups), !knownScope || groups.Length == 0);
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
