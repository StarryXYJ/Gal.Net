using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using GalNet.Core.I18n;

namespace GalNet.Editor.Abstraction.Settings;

/// <summary>
/// 编辑器全局设置 —— 持久化在编辑器配置目录。
/// </summary>
public sealed class EditorSettings
{
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>编辑器 UI 语言</summary>
    public I18nLocale UiLocale { get; set; } = I18nLocale.ZhCn;

    /// <summary>Avalonia 主题</summary>
    public string Theme { get; set; } = "Violet";

    /// <summary>Editor body font size in device-independent pixels.</summary>
    public double FontSize { get; set; } = 12;

    /// <summary>Last automatically saved editor dock view.</summary>
    [JsonConverter(typeof(EmbeddedJsonStringConverter))]
    public string? LastDockLayout { get; set; }

    /// <summary>User-named dock layouts. Default is code-defined and not stored here.</summary>
    public Dictionary<string, string> DockLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近打开的项目列表（含名称、路径、最后打开时间）</summary>
    public List<RecentProjectInfo> RecentProjects { get; set; } = [];

    /// <summary>最近项目列表最大保留数</summary>
    public int MaxRecentProjects { get; set; } = 10;

    public bool AutoSaveProject { get; set; } = true;

    /// <summary>Whether newly inserted Group entries show their parameter fields.</summary>
    public bool ExpandNewEntries { get; set; }

    /// <summary>Stable UI command ID to Avalonia gesture text. Empty text disables the shortcut.</summary>
    public Dictionary<string, string> GestureOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
