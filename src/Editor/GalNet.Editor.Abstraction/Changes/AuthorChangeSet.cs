namespace GalNet.Editor.Abstraction.Changes;

public enum AuthorChangeCause { Execute, Undo, Redo }

[Flags]
public enum GroupChangeKind
{
    None = 0,
    Condition = 1,
    Parameters = 2,
    EntryType = 4,
    Membership = 8,
    Order = 16
}

/// <summary>A committed change range; indexes describe the earliest affected prefix, not an object identity.</summary>
public sealed record AuthorGroupChange(string GroupId, int FirstIndex, GroupChangeKind Kind, IReadOnlyList<string> EntryIds);

/// <summary>One successful content transaction. Unknown scope conservatively covers legacy edits.</summary>
public sealed record AuthorEntryMove(string SourceGroupId, string SourceEntryId, string TargetGroupId, string TargetEntryId);
public sealed record AuthorChangeSet(long Revision, AuthorChangeCause Cause, IReadOnlyList<AuthorGroupChange> Groups, bool UnknownScope)
{
    public IReadOnlyList<AuthorEntryMove> Moves { get; init; } = [];
}
