namespace GalNet.Contracts.Builtins.Audio;

public sealed record AudioBusDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float DefaultGain { get; init; } = 1f;
}

/// <summary>Validated, owned audio declarations. Empty configurations and ungrouped tracks are valid.</summary>
public sealed class AudioConfiguration
{
    public static AudioConfiguration Empty { get; } = new();
    private readonly Dictionary<string, AudioTrackDefinition> _tracks;
    public IReadOnlyList<AudioBusDefinition> Buses { get; }
    public IReadOnlyList<AudioTrackDefinition> Tracks { get; }

    public AudioConfiguration(IEnumerable<AudioTrackDefinition>? tracks = null, IEnumerable<AudioBusDefinition>? buses = null)
    {
        var groupArray = (buses ?? []).ToArray();
        var busIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groupArray)
            if (group is null || string.IsNullOrWhiteSpace(group.Id) || !busIds.Add(group.Id) || !float.IsFinite(group.DefaultGain) || group.DefaultGain < 0)
                throw new InvalidDataException("Audio groups require unique, nonempty IDs.");

        var trackArray = (tracks ?? []).ToArray();
        _tracks = new(StringComparer.Ordinal);
        foreach (var track in trackArray)
        {
            if (track is null || string.IsNullOrWhiteSpace(track.Id) || !_tracks.TryAdd(track.Id, track))
                throw new InvalidDataException("Audio tracks require unique, nonempty IDs.");
            if (track.BusId is not null && !busIds.Contains(track.BusId))
                throw new InvalidDataException($"Audio track '{track.Id}' references unknown bus '{track.BusId}'.");
            if (!float.IsFinite(track.DefaultGain) || track.DefaultGain < 0 || track.MaxVoices < 1 || track.MaxQueuedItems < 0 ||
                !Enum.IsDefined(track.OverflowPolicy) || !Enum.IsDefined(track.QueueOverflowPolicy) || !Enum.IsDefined(track.PlaybackOrder) || !Enum.IsDefined(track.Repeat))
                throw new InvalidDataException($"Audio track '{track.Id}' has invalid gain, capacity, or overflow policy.");
        }
        Buses = Array.AsReadOnly(groupArray);
        Tracks = Array.AsReadOnly(trackArray);
    }

    public bool TryGetTrack(string id, out AudioTrackDefinition track) => _tracks.TryGetValue(id, out track!);
    public bool HasTarget(SEffectScope scope, string id) => scope == SEffectScope.Track ? _tracks.ContainsKey(id) : scope == SEffectScope.Bus && Buses.Any(bus => bus.Id == id);
}
