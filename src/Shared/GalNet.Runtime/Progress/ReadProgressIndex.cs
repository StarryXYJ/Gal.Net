namespace GalNet.Runtime.Progress;

public readonly record struct ReadEntryId(string GroupId, string EntryId);

/// <summary>In-memory read progress. The owner serializes access and chooses when to persist snapshots.</summary>
public sealed class ReadProgressIndex(IEnumerable<ReadEntryId>? entries = null)
{
    private readonly HashSet<ReadEntryId> _entries = entries?.ToHashSet() ?? [];
    public long Revision { get; private set; }
    public bool IsRead(string groupId, string entryId) => _entries.Contains(new(groupId, entryId));
    public void MarkRead(string groupId, string entryId)
    {
        if (_entries.Add(new(groupId, entryId))) Revision++;
    }
    public ReadEntryId[] Snapshot() => _entries.ToArray();
    public void Clear() { _entries.Clear(); Revision++; }
}
