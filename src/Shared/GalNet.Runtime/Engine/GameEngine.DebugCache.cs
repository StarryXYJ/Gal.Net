using System.Text;

namespace GalNet.Runtime.Engine;

public sealed record GameDebugCacheStats(int Groups, long PayloadBytes, long BudgetBytes, int GroupLimit);
public sealed record GameDebugReplayStats(double Milliseconds, int Instructions, bool EntranceCacheHit);

public sealed partial class GameEngine
{
    public sealed partial class DebugSession
    {
        private readonly Dictionary<string, long> _entranceBytes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _entranceUse = new(StringComparer.Ordinal);
        private long _cacheClock;
        private long _cacheBytes;
        private int _replayedInstructions;
        private GameDebugReplayStats? _lastReplay;
        internal void RecordReplayInstruction() => _replayedInstructions++;

        private void ClearEntrances()
        {
            _entrances.Clear(); _entranceBytes.Clear(); _entranceUse.Clear(); _cacheBytes = 0; _lastReplay = null;
        }
        private void RemoveEntrance(string group)
        {
            _entrances.Remove(group);
            if (_entranceBytes.Remove(group, out var bytes)) _cacheBytes -= bytes;
            _entranceUse.Remove(group);
        }
        private void StoreEntrance(string group)
        {
            RemoveEntrance(group);
            if (_options.MaxEntranceGroups == 0 || _options.MaxEntrancePayloadBytes == 0) return;
            var snapshot = CaptureScene();
            // A bounded serialized payload budget, not an estimate of managed heap or backend resources.
            var bytes = 128L + Encoding.UTF8.GetByteCount(group);
            foreach (var (key, section) in snapshot.States)
                bytes += 32L + Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(section.Payload.GetRawText());
            if (bytes > _options.MaxEntrancePayloadBytes) return;
            while (_entrances.Count > 0 && (_entrances.Count >= _options.MaxEntranceGroups || _cacheBytes + bytes > _options.MaxEntrancePayloadBytes))
                RemoveEntrance(_entranceUse.MinBy(pair => pair.Value).Key);
            _entrances[group] = snapshot;
            _entranceBytes[group] = bytes;
            _entranceUse[group] = ++_cacheClock;
            _cacheBytes += bytes;
        }
    }
}
