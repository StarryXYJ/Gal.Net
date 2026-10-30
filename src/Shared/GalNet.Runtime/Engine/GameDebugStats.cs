namespace GalNet.Runtime.Engine;

public sealed record GameDebugCacheStats(int Groups, long PayloadBytes, long BudgetBytes, int GroupLimit);
public sealed record GameDebugReplayStats(double Milliseconds, int Instructions, bool EntranceCacheHit);
