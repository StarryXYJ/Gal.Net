using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using GalNet.Core.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Dock;
using GalNet.Editor.Models;
using GalNet.Editor.Project;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

/// <summary>
/// 编辑器主页面 ViewModel —— 打开项目后显示的编辑界面。
/// 管理 Dock 布局和所有面板的生命周期。
/// </summary>
public partial class EditorPageViewModel : PageViewModelBase, IMenuProvider
{
    private readonly INavigationService _navigation;
    private readonly IProjectService _projectService;
    private readonly CommandService _commandService;
    private readonly EditorDockFactory _dockFactory;
    private readonly IEditorWindowFactory _windowFactory;

    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>当前项目名称（仅用于 UI 显示）</summary>
    public string ProjectName => _projectService.Current?.Name ?? "";

    /// <summary>菜单项集合 —— 由 SideMenu 控件绑定</summary>
    public IList<MenuData> MenuItems { get; } = new AvaloniaList<MenuData>();

    /// <summary>Dock 布局根节点 —— 绑定到 DockControl</summary>
    public IRootDock? Layout { get; private set; }

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
        CommandService commandService,
        EditorDockFactory dockFactory,
        IEditorWindowFactory windowFactory)
    {
        _navigation = navigation;
        _projectService = projectService;
        _commandService = commandService;
        _dockFactory = dockFactory;
        _windowFactory = windowFactory;

        var project = _projectService.Current
            ?? throw new InvalidOperationException("EditorPageViewModel requires an open project");
        Title = $"GalNet Editor — {project.Name}";
        StatusText = $"项目: {project.Name}  |  路径: {project.RootPath}";

        // ── 初始化 Dock 布局 ──
        InitializeDock();

        // ── 构建菜单数据 ──
        BuildMenuItems();
    }

    // ═══════════════════════════════════════════
    //  Dock 布局
    // ═══════════════════════════════════════════

    private void InitializeDock()
    {
        Layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);
    }

    // ═══════════════════════════════════════════
    //  弹出设置窗口
    // ═══════════════════════════════════════════

    [RelayCommand]
    private async Task ShowProjectSettingsAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var window = _windowFactory.CreateProjectSettingsWindow();
        await window.ShowDialog(mainWindow);
    }

    [RelayCommand]
    private async Task ShowEditorSettingsAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var window = _windowFactory.CreateEditorSettingsWindow();
        await window.ShowDialog(mainWindow);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    // ═══════════════════════════════════════════
    //  菜单数据构建
    // ═══════════════════════════════════════════

    private void BuildMenuItems()
    {
        var saveCmd = _commandService.GetCommand<SaveProjectCommand>();
        var closeCmd = _commandService.GetCommand<CloseProjectCommand>();

        var items = new AvaloniaList<MenuData>
        {
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
            new()
            {
                Header = "编辑",
                Children = new AvaloniaList<MenuData>
                {
                    new() { Header = "撤销", InputGesture = new Avalonia.Input.KeyGesture(Key.Z, KeyModifiers.Control), Command = UndoCommand },
                    new() { Header = "重做", InputGesture = new Avalonia.Input.KeyGesture(Key.Y, KeyModifiers.Control), Command = RedoCommand },
                }
            },
            new()
            {
                Header = "设置",
                Children = new AvaloniaList<MenuData>
                {
                    new() { Header = "项目设置...", Command = ShowProjectSettingsCommand },
                    new() { Header = "编辑器设置...", Command = ShowEditorSettingsCommand },
                }
            },
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
