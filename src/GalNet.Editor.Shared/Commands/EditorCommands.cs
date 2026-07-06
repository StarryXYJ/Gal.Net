using CommunityToolkit.Mvvm.Input;

namespace GalNet.Editor.Shared.Commands;

/// <summary>
/// 编辑器全局命令 —— 纯 UI 命令（与 DI 无关）。
/// 依赖注入的命令请使用 EditorCommand 派生类 + CommandService。
/// </summary>
public static class EditorCommands
{
    // ── 编辑菜单 ──

    /// <summary>撤销 (Ctrl+Z)</summary>
    public static RelayCommand UndoCommand { get; set; } = new(() => { }, () => false);

    /// <summary>重做 (Ctrl+Y)</summary>
    public static RelayCommand RedoCommand { get; set; } = new(() => { }, () => false);

    // ── 窗口菜单 ──

    /// <summary>切换面板可见性</summary>
    public static RelayCommand<string> TogglePanelCommand { get; set; } =
        new(_ => { });

    /// <summary>保存当前窗口布局</summary>
    public static RelayCommand SaveLayoutCommand { get; set; } = new(() => { });

    /// <summary>加载保存的窗口布局</summary>
    public static RelayCommand LoadLayoutCommand { get; set; } = new(() => { });

    /// <summary>重置窗口布局为默认</summary>
    public static RelayCommand ResetLayoutCommand { get; set; } = new(() => { });
}
