namespace GalNet.Core.Scene;

/// <summary>Replayable description of a handle-controlled looping animation.</summary>
public sealed class ActiveAnimationState
{
    public string EntryType { get; init; } = "";
    public string PlaybackHandleId { get; init; } = "";
    /// <summary>Original primitive entry values, including the complete plan JSON when applicable.</summary>
    public Dictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
}
