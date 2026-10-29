using System.Text.Json.Serialization;

namespace GalNet.Contracts.Builtins.Audio;

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

public enum AudioPlaybackOrder { Sequential, Random }
public enum AudioTrackRepeat { None, One, All }

/// <summary>Author-configured logical route, independent from backend players.</summary>
public sealed record AudioTrackDefinition
{
    public AudioTrackDefinition() { }

    // Source-generated JSON binds init-only members through constructor metadata.
    // Explicit optional defaults prevent omitted fields from replacing CLR initializers with zero.
    [JsonConstructor]
    public AudioTrackDefinition(string id, string name = "", string? busId = null,
        AudioPlaybackOrder playbackOrder = AudioPlaybackOrder.Sequential, AudioTrackRepeat repeat = AudioTrackRepeat.None,
        bool preserveDuringCatchUp = false, float defaultGain = 1f, int maxVoices = 1, int maxQueuedItems = 0,
        AudioOverflowPolicy overflowPolicy = AudioOverflowPolicy.DropNewest,
        AudioQueueOverflowPolicy queueOverflowPolicy = AudioQueueOverflowPolicy.DropNewest)
    {
        Id = id; Name = name; BusId = busId; PlaybackOrder = playbackOrder; Repeat = repeat;
        PreserveDuringCatchUp = preserveDuringCatchUp; DefaultGain = defaultGain; MaxVoices = maxVoices;
        MaxQueuedItems = maxQueuedItems; OverflowPolicy = overflowPolicy; QueueOverflowPolicy = queueOverflowPolicy;
    }

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? BusId { get; init; }
    public AudioPlaybackOrder PlaybackOrder { get; init; }
    public AudioTrackRepeat Repeat { get; init; }
    public bool PreserveDuringCatchUp { get; init; }
    public float DefaultGain { get; init; } = 1f;
    public int MaxVoices { get; init; } = 1;
    public int MaxQueuedItems { get; init; }
    public AudioOverflowPolicy OverflowPolicy { get; init; } = AudioOverflowPolicy.DropNewest;
    public AudioQueueOverflowPolicy QueueOverflowPolicy { get; init; } = AudioQueueOverflowPolicy.DropNewest;
}
