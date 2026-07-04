using GalNet.Core.I18n;

namespace GalNet.Core.Settings;

/// <summary>
/// 编辑器全局设置 —— 持久化在编辑器配置目录。
/// </summary>
public sealed class EditorSettings : SettingsSection
{
    public override string SectionKey => "editor";

    /// <summary>编辑器 UI 语言</summary>
    public I18nLocale UiLocale { get; set; } = I18nLocale.ZhCn;

    /// <summary>Avalonia 主题</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Dock 布局序列化数据（保存窗口面板布局）</summary>
    public string? DockLayout { get; set; }

    /// <summary>最近打开的项目路径列表</summary>
    public List<string> RecentProjects { get; set; } = [];
}
