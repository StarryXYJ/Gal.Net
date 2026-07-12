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
    public string Theme { get; set; } = "Violet";

    /// <summary>Dock 布局序列化数据（保存窗口面板布局）</summary>
    public string? DockLayout { get; set; }

    /// <summary>最近打开的项目列表（含名称、路径、最后打开时间）</summary>
    public List<RecentProjectInfo> RecentProjects { get; set; } = [];

    /// <summary>最近项目列表最大保留数</summary>
    public int MaxRecentProjects { get; set; } = 10;

    public bool AutoSaveProject { get; set; } = true;
}
