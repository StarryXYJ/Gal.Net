using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Editor.Models;

/// <summary>
/// 菜单项数据模型 —— 抽象描述一个菜单项，包含显示文本、快捷键、命令等。
/// 支持嵌套子菜单（Children）和分隔线（IsSeparator）。
/// </summary>
public partial class MenuData : ObservableObject
{
    /// <summary>菜单项显示文本（直接文本，优先级高于 HeaderKey）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeader))]
    private string? _header;

    public bool HasHeader => !string.IsNullOrWhiteSpace(Header);

    /// <summary>i18n key，用于国际化查找显示文本</summary>
    public string? HeaderKey { get; set; }

    /// <summary>键盘快捷键，如 Ctrl+S</summary>
    public KeyGesture? InputGesture { get; set; }

    /// <summary>点击执行的命令</summary>
    public ICommand? Command { get; set; }

    /// <summary>命令参数</summary>
    public object? CommandParameter { get; set; }

    /// <summary>子菜单项（用于多级菜单）</summary>
    public IList<MenuData>? Children { get; set; }

    /// <summary>是否为分隔线（true 时不渲染为菜单项，而是 Separator）</summary>
    public bool IsSeparator { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Render the item as a checkable menu entry.</summary>
    public bool IsCheckable { get; set; }

    [ObservableProperty]
    private bool _isChecked;
}
