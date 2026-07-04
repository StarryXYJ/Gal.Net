using GalNet.Core.I18n;

namespace GalNet.Core.Settings;

/// <summary>
/// 启动器全局设置 —— 持久化在启动器配置目录。
/// </summary>
public sealed class LauncherSettings : SettingsSection
{
    public override string SectionKey => "launcher";

    /// <summary>启动器 UI 语言</summary>
    public I18nLocale UiLocale { get; set; } = I18nLocale.ZhCn;

    /// <summary>启动器窗口宽度</summary>
    public int Width { get; set; } = 1280;

    /// <summary>启动器窗口高度</summary>
    public int Height { get; set; } = 720;

    /// <summary>默认音频输出设备</summary>
    public string? AudioDevice { get; set; }
}
