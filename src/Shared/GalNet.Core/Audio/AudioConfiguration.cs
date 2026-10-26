namespace GalNet.Core.Audio;

public sealed record AudioGroupDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

/// <summary>Validated, owned audio declarations. Empty configurations and ungrouped tracks are valid.</summary>
public sealed class AudioConfiguration
{
    public static AudioConfiguration Empty { get; } = new();
    private readonly Dictionary<string, AudioTrackDefinition> _tracks;
    public IReadOnlyList<AudioGroupDefinition> Groups { get; }
    public IReadOnlyList<AudioTrackDefinition> Tracks { get; }

    public AudioConfiguration(IEnumerable<AudioTrackDefinition>? tracks = null, IEnumerable<AudioGroupDefinition>? groups = null)
    {
        var groupArray = (groups ?? []).ToArray();
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groupArray)
            if (group is null || string.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id))
                throw new InvalidDataException("Audio groups require unique, nonempty IDs.");

        var trackArray = (tracks ?? []).ToArray();
        _tracks = new(StringComparer.Ordinal);
        foreach (var track in trackArray)
        {
            if (track is null || string.IsNullOrWhiteSpace(track.Id) || !_tracks.TryAdd(track.Id, track))
                throw new InvalidDataException("Audio tracks require unique, nonempty IDs.");
            if (track.GroupId is not null && !groupIds.Contains(track.GroupId))
                throw new InvalidDataException($"Audio track '{track.Id}' references unknown group '{track.GroupId}'.");
            if (!float.IsFinite(track.DefaultGain) || track.DefaultGain < 0 || track.MaxVoices < 1 || track.MaxQueuedItems < 0 ||
                !Enum.IsDefined(track.OverflowPolicy) || !Enum.IsDefined(track.QueueOverflowPolicy))
                throw new InvalidDataException($"Audio track '{track.Id}' has invalid gain, capacity, or overflow policy.");
        }
        Groups = Array.AsReadOnly(groupArray);
        Tracks = Array.AsReadOnly(trackArray);
    }

    public bool TryGetTrack(string id, out AudioTrackDefinition track) => _tracks.TryGetValue(id, out track!);
}
