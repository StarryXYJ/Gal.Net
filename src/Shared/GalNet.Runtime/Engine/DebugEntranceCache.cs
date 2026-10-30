using System.Diagnostics.CodeAnalysis;
using System.Text;
using GalNet.Core.Runtime;

namespace GalNet.Runtime.Engine;

/// <summary>Owns entrance snapshots and their retention policy. The caller holds the engine execution gate.</summary>
internal sealed class DebugEntranceCache(long budgetBytes, int groupLimit)
{
    private sealed record Entrance(GameSnapshot Snapshot, long Bytes)
    {
        public long LastUse { get; set; }
    }

    private readonly Dictionary<string, Entrance> _entries = new(StringComparer.Ordinal);
    private long _clock;
    private long _bytes;

    public IEnumerable<string> Groups => _entries.Keys;
    public GameDebugCacheStats Stats => new(_entries.Count, _bytes, budgetBytes, groupLimit);

    public void Clear()
    {
        _entries.Clear();
        _bytes = 0;
    }

    // The synchronous factory transfers a detached snapshot; it is never retained or called when disabled.
    public void Capture(string group, Func<GameSnapshot> capture)
    {
        Remove(group);
        if (groupLimit == 0 || budgetBytes == 0) return;
        var snapshot = capture();
        // A serialized payload budget, not an estimate of managed heap or backend resources.
        var bytes = 128L + Encoding.UTF8.GetByteCount(group);
        foreach (var (key, section) in snapshot.States)
            bytes += 32L + Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(section.Payload.GetRawText());
        if (bytes > budgetBytes) return;
        while (_entries.Count > 0 && (_entries.Count >= groupLimit || _bytes + bytes > budgetBytes))
            Remove(_entries.MinBy(pair => pair.Value.LastUse).Key);
        _entries[group] = new(snapshot, bytes) { LastUse = ++_clock };
        _bytes += bytes;
    }

    /// <summary>Returns an independent snapshot and refreshes its retention priority.</summary>
    public bool TryGet(string group, [NotNullWhen(true)] out GameSnapshot? snapshot)
    {
        snapshot = null;
        if (!_entries.TryGetValue(group, out var entry)) return false;
        snapshot = SnapshotCopy.Clone(entry.Snapshot);
        entry.LastUse = ++_clock;
        return true;
    }

    private void Remove(string group)
    {
        if (_entries.Remove(group, out var entry)) _bytes -= entry.Bytes;
    }
}
