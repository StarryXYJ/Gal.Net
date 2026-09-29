namespace GalNet.Core.Scene;

/// <summary>Replayable description of a handle-controlled looping animation.</summary>
public sealed class ActiveAnimationState
{
    public string EntryType { get; init; } = "";
    public string PlaybackHandleId { get; init; } = "";
    /// <summary>Defaults to Loop so saves written before this field existed retain their prior meaning.</summary>
    public AnimationLoopMode LoopMode { get; init; } = AnimationLoopMode.Loop;
    /// <summary>Original primitive entry values, including the complete plan JSON when applicable.</summary>
    public Dictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
}
