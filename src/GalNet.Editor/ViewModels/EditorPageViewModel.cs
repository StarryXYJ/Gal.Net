using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Models;
using GalNet.Editor.Project;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

/// <summary>
/// 编辑器主页面 ViewModel —— 打开项目后显示的编辑界面。
/// 实现 IMenuProvider 向 MainWindow 提供菜单数据。
/// 所有命令通过 CommandService 统一管理。
/// </summary>
public partial class EditorPageViewModel : PageViewModelBase, IMenuProvider
{
    private readonly INavigationService _navigation;
    private readonly IProjectService _projectService;
    private readonly CommandService _commandService;

    public GalProject Project { get; }

    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>菜单项集合 —— 由 SideMenu 控件绑定</summary>
    public IList<MenuData> MenuItems { get; } = new AvaloniaList<MenuData>();

    // ── 纯 UI 命令（与 DI 无关） ──

    /// <summary>撤销 (Ctrl+Z)</summary>
    public ICommand UndoCommand { get; } = new RelayCommand(() => { }, () => false);

    /// <summary>重做 (Ctrl+Y)</summary>
    public ICommand RedoCommand { get; } = new RelayCommand(() => { }, () => false);

    /// <summary>切换面板可见性</summary>
    public ICommand TogglePanelCommand { get; } = new RelayCommand<string>(_ => { });

    // ── 布局命令 ──

    public ICommand SaveLayoutCommand { get; } = new RelayCommand(() => { });
    public ICommand LoadLayoutCommand { get; } = new RelayCommand(() => { });
    public ICommand ResetLayoutCommand { get; } = new RelayCommand(() => { });

    public EditorPageViewModel(
        INavigationService navigation,
        IProjectService projectService,
        CommandService commandService)
    {
        _navigation = navigation;
        _projectService = projectService;
        _commandService = commandService;
        Project = _projectService.Current!;
        Title = $"GalNet Editor — {Project.Name}";
        StatusText = $"项目: {Project.Name}  |  路径: {Project.RootPath}";

        // ── 构建菜单数据 ──
        BuildMenuItems();
    }

    // ═══════════════════════════════════════════
    //  菜单数据构建
    // ═══════════════════════════════════════════

    private void BuildMenuItems()
    {
        // 从 CommandService 获取 DI 管理的命令
        var saveCmd = _commandService.GetCommand<SaveProjectCommand>();
        var closeCmd = _commandService.GetCommand<CloseProjectCommand>();

        var items = new AvaloniaList<MenuData>
        {
            // ── 文件 ──
            new()
            {
                Header = "文件",
                Children = new AvaloniaList<MenuData>
                {
                    new() { Header = saveCmd.DisplayName, InputGesture = saveCmd.Gesture, Command = saveCmd.Command },
                    new() { IsSeparator = true },
                    new() { Header = closeCmd.DisplayName, Command = closeCmd.Command },
                    new() { IsSeparator = true },
                    new() { Header = "退出", InputGesture = new Avalonia.Input.KeyGesture(Key.F4, KeyModifiers.Alt), IsEnabled = false },
                }
            },

            // ── 编辑 ──
            new()
            {
                Header = "编辑",
                Children = new AvaloniaList<MenuData>
                {
                    new() { Header = "撤销", InputGesture = new Avalonia.Input.KeyGesture(Key.Z, KeyModifiers.Control), Command = UndoCommand },
                    new() { Header = "重做", InputGesture = new Avalonia.Input.KeyGesture(Key.Y, KeyModifiers.Control), Command = RedoCommand },
                }
            },

            // ── 查看 ──
            new()
            {
                Header = "查看",
                Children = new AvaloniaList<MenuData>
                {
                    new() { Header = "项目浏览器", Command = TogglePanelCommand, CommandParameter = "project-explorer" },
                    new() { Header = "属性面板",   Command = TogglePanelCommand, CommandParameter = "inspector" },
                    new() { Header = "日志面板",   Command = TogglePanelCommand, CommandParameter = "log" },
                    new() { Header = "游戏预览",   Command = TogglePanelCommand, CommandParameter = "game-preview" },
                }
            },

            // ── 窗口 ──
            new()
            {
                Header = "窗口",
                Children = new AvaloniaList<MenuData>
                {
                    new() { Header = "保存窗口布局", Command = SaveLayoutCommand },
                    new() { Header = "加载窗口布局", Command = LoadLayoutCommand },
                    new() { Header = "重置为默认布局", Command = ResetLayoutCommand },
                }
            },

            // ── 帮助 ──
            new()
            {
                Header = "帮助",
                Children = new AvaloniaList<MenuData>
                {
                    new() { Header = "关于 GalNet Editor", IsEnabled = false },
                }
            },
        };

        MenuItems.Clear();
        foreach (var item in items)
            MenuItems.Add(item);
    }
}
