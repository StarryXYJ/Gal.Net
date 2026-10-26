using System.Text.Json;

namespace GalNet.Contracts.Builtins.Audio;

/// <summary>Only logical playback data; backend IDs and tickets are deliberately separate.</summary>
public sealed class AudioItemState
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int CompletedIterations { get; set; }
    public AudioPlayRequest Request { get; set; } = new("", "", AudioPlacement.Enqueue);
}

public sealed class AudioTrackState
{
    public string Id { get; init; } = "";
    public bool Paused { get; set; }
    public List<AudioItemState> Active { get; init; } = [];
    public List<AudioItemState> Pending { get; init; } = [];
}

public sealed record AudioSEffectState(string TrackId, string InstanceId, string SEffectId, JsonElement Parameters)
{
    // JSON array encoding preserves the existing track-local identity without separator collisions.
    public static string Key(string trackId, string instanceId) => JsonSerializer.Serialize(new[] { trackId, instanceId });
}
