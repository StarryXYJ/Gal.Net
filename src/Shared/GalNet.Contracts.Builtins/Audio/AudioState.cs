using System.Text.Json;
using GalNet.Core.Serialization;

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
    public uint RandomState { get; set; } = 0x9e3779b9;
    public List<AudioItemState> Active { get; init; } = [];
    public List<AudioItemState> Pending { get; init; } = [];
}

public sealed record AudioSEffectState(string TargetId, string InstanceId, string SEffectId, JsonElement Parameters, SEffectScope Scope = SEffectScope.Track)
{
    // JSON array encoding preserves the existing track-local identity without separator collisions.
    public static string Key(string trackId, string instanceId) => JsonCodec.Serialize(new[] { trackId, instanceId });
    public string Key() => Scope == SEffectScope.Track ? Key(TargetId, InstanceId) : JsonCodec.Serialize(new[] { "bus", TargetId, InstanceId });
}

public sealed record AudioChannelState(string TargetId, SEffectScope Scope, float Gain = 1, bool Muted = false)
{
    public string Key() => JsonCodec.Serialize(new[] { Scope.ToString(), TargetId });
}
