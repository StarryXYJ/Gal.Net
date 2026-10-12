namespace GalNet.Core.Settings;

/// <summary>
/// 运行时游戏设置 —— 可被项目设置覆盖的玩家偏好。
/// 持久化在用户目录下，按游戏隔离。
/// </summary>
public sealed class GameSettings : SettingsSection
{
    public override string SectionKey => "game";

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

    /// <summary>全屏模式</summary>
    public bool Fullscreen { get; set; } = true;
}
