using GalNet.Core.Audio;
using GalNet.Core.Execution;
using GalNet.Runtime.Audio;

namespace GalNet.Avalonia.GameHost.Settings;

/// <summary>
/// Default Avalonia host preferences, projected to the individual runtime modules.
/// Projects may use their own model; the engine and PlayerSession do not depend on this type.
/// </summary>
public sealed class GameSettings
{

    /// <summary>文本显示速度（字符/秒，0 = 即时）</summary>
    public float TextSpeed { get; set; } = 30f;

    /// <summary>自动推进间隔（整数毫秒）</summary>
    public int AutoAdvanceIntervalMilliseconds { get; set; } = 2000;

    /// <summary>Delay used by quick mode between reading units, in integer milliseconds.</summary>
    public int QuickAdvanceIntervalMilliseconds { get; set; } = 50;

    /// <summary>Quick mode stops before text not read before this visit.</summary>
    public bool QuickReadOnly { get; set; } = true;

    /// <summary>BGM 默认音量（0~1）</summary>
    public float BgmVolume { get; set; } = 1f;

    /// <summary>音效默认音量（0~1）</summary>
    public float SfxVolume { get; set; } = 1f;

    /// <summary>语音默认音量（0~1）</summary>
    public float VoiceVolume { get; set; } = 1f;

    public float MasterVolume { get; set; } = 1f;
    public bool MasterMuted { get; set; }
    public bool BgmMuted { get; set; }
    public bool SfxMuted { get; set; }
    public bool VoiceMuted { get; set; }

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
        Groups = new Dictionary<string, AudioMixLevel>(StringComparer.Ordinal)
        {
            [AudioBuses.Bgm] = new(BgmVolume, BgmMuted),
            [AudioBuses.Sfx] = new(SfxVolume, SfxMuted),
            [AudioBuses.Vocal] = new(VoiceVolume, VoiceMuted)
        }
    };
}
