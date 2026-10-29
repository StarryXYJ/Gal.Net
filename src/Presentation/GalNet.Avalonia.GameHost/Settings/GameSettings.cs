using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Execution;

namespace GalNet.Avalonia.GameHost.Settings;

/// <summary>
/// Default Avalonia host preferences, projected to the individual runtime modules.
/// Projects may use their own model; the engine and PlayerSession do not depend on this type.
/// </summary>
public sealed class GameSettings
{

    /// <summary>文本显示速度（字符/秒，0 = 即时）</summary>
    public float TextSpeed { get; set; } = 30f;
    public bool EnableReadTextColor { get; set; }
    public string ReadTextColor { get; set; } = "#A8B8C8";

    /// <summary>自动推进间隔（整数毫秒）</summary>
    public int AutoAdvanceIntervalMilliseconds { get; set; } = 2000;

    /// <summary>Delay used by quick mode between reading units, in integer milliseconds.</summary>
    public int QuickAdvanceIntervalMilliseconds { get; set; } = 50;

    /// <summary>Quick mode stops before text not read before this visit.</summary>
    public bool QuickReadOnly { get; set; } = true;

    public float MasterVolume { get; set; } = 1f;
    public bool MasterMuted { get; set; }
    public Dictionary<string, AudioMixLevel> Buses { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, AudioMixLevel> Tracks { get; } = new(StringComparer.Ordinal);

    public PlaybackOptions ToPlaybackOptions() => new()
    {
        AutoAdvanceIntervalMilliseconds = AutoAdvanceIntervalMilliseconds,
        QuickAdvanceIntervalMilliseconds = QuickAdvanceIntervalMilliseconds,
        QuickReadOnly = QuickReadOnly
    };

    public AudioMixOptions ToAudioMixOptions() => new()
    {
        MasterGain = MasterVolume,
        MasterMuted = MasterMuted,
        Buses = Buses,
        Tracks = Tracks
    };
}
