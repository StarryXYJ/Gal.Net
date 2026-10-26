namespace GalNet.Core.Audio;

public enum AudioOverflowPolicy
{
    DropNewest,
    StopOldest
}

public enum AudioQueueOverflowPolicy
{
    DropNewest,
    DropOldest
}

/// <summary>Author-configured logical route, independent from backend players.</summary>
public sealed record AudioTrackDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? GroupId { get; init; }
    public bool PreserveDuringCatchUp { get; init; }
    public float DefaultGain { get; init; } = 1f;
    public int MaxVoices { get; init; } = 1;
    public int MaxQueuedItems { get; init; }
    public AudioOverflowPolicy OverflowPolicy { get; init; } = AudioOverflowPolicy.DropNewest;
    public AudioQueueOverflowPolicy QueueOverflowPolicy { get; init; } = AudioQueueOverflowPolicy.DropNewest;
}
