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

    /// <summary>自动推进间隔（秒）</summary>
    public float AutoAdvanceInterval { get; set; } = 2f;

    /// <summary>Delay used by quick/skip mode between entries.</summary>
    public float QuickAdvanceInterval { get; set; } = 0.05f;

    /// <summary>跳过模式：All = 全部, ReadOnly = 仅已读</summary>
    public string SkipMode { get; set; } = "ReadOnly";

    /// <summary>BGM 默认音量（0~1）</summary>
    public float BgmVolume { get; set; } = 0.8f;

    /// <summary>音效默认音量（0~1）</summary>
    public float SfxVolume { get; set; } = 1f;

    /// <summary>语音默认音量（0~1）</summary>
    public float VoiceVolume { get; set; } = 1f;

    /// <summary>全屏模式</summary>
    public bool Fullscreen { get; set; } = true;
}
