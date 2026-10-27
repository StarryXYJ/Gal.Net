using System.Collections.ObjectModel;

namespace GalNet.Contracts.Builtins.Audio;

public sealed record AudioMixLevel(float Gain = 1f, bool Muted = false);

/// <summary>Host-projected mixer values. Audio never reads a player preference model.</summary>
public sealed record AudioMixOptions
{
    public float MasterGain { get; init; } = 1f;
    public bool MasterMuted { get; init; }
    public IReadOnlyDictionary<string, AudioMixLevel> Buses { get; init; } = ReadOnlyDictionary<string, AudioMixLevel>.Empty;
    public IReadOnlyDictionary<string, AudioMixLevel> Tracks { get; init; } = ReadOnlyDictionary<string, AudioMixLevel>.Empty;

    public AudioMixOptions Snapshot()
    {
        ValidateGain(MasterGain);
        return this with { Buses = CopyLevels(Buses), Tracks = CopyLevels(Tracks) };
    }

    private static IReadOnlyDictionary<string, AudioMixLevel> CopyLevels(IReadOnlyDictionary<string, AudioMixLevel> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var groups = new Dictionary<string, AudioMixLevel>(StringComparer.Ordinal);
        foreach (var (id, level) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(level);
            ValidateGain(level.Gain);
            groups.Add(id, level);
        }
        return new ReadOnlyDictionary<string, AudioMixLevel>(groups);
    }

    private static void ValidateGain(float gain)
    {
        if (!float.IsFinite(gain) || gain < 0 || gain > 1)
            throw new ArgumentOutOfRangeException(nameof(gain), "Mixer gain must be finite and between zero and one.");
    }
}
