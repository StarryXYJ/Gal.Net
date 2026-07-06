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

public partial class EditorPageViewModel : PageViewModelBase, IMenuProvider
{
    private readonly IProjectService _projectService;
    private readonly CommandService _commandService;
    private readonly EditorDockFactory _dockFactory;
    private readonly IEditorWindowFactory _windowFactory;

    public IEditorLocalizationService L { get; }

    [ObservableProperty]
    private string _statusText = "";

    public string ProjectName => _projectService.Current?.Name ?? "";

    public IList<MenuData> MenuItems { get; } = new AvaloniaList<MenuData>();

    public IRootDock? Layout { get; private set; }

    public ICommand UndoCommand { get; } = new RelayCommand(() => { }, () => false);
    public ICommand RedoCommand { get; } = new RelayCommand(() => { }, () => false);
    public ICommand TogglePanelCommand { get; } = new RelayCommand<string>(_ => { });

    public ICommand SaveLayoutCommand { get; } = new RelayCommand(() => { });
    public ICommand LoadLayoutCommand { get; } = new RelayCommand(() => { });
    public ICommand ResetLayoutCommand { get; } = new RelayCommand(() => { });

    public EditorPageViewModel(
        INavigationService navigation,
        IProjectService projectService,
        CommandService commandService,
        EditorDockFactory dockFactory,
        IEditorWindowFactory windowFactory,
        IEditorLocalizationService localization)
    {
        _projectService = projectService;
        _commandService = commandService;
        _dockFactory = dockFactory;
        _windowFactory = windowFactory;
        L = localization;

        var project = _projectService.Current
            ?? throw new InvalidOperationException("EditorPageViewModel requires an open project");

        UpdateLocalizedText();
        InitializeDock();
        BuildMenuItems();

        L.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "Item[]") return;
            UpdateLocalizedText();
        };
    }

    private void UpdateLocalizedText()
    {
        var project = _projectService.Current;
        Title = project is null ? L["App.Title"] : $"{L["App.Title"]} - {project.Name}";
        StatusText = project is null
            ? L["Editor.Status.Ready"]
            : L.Format("Editor.Status.Project", project.Name, project.RootPath);
    }

    private void InitializeDock()
    {
        Layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);
    }

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

    private void BuildMenuItems()
    {
        var saveCmd = _commandService.GetCommand<SaveProjectCommand>();
        var closeCmd = _commandService.GetCommand<CloseProjectCommand>();

        var items = new AvaloniaList<MenuData>
        {
            new()
            {
                HeaderKey = "Editor.Menu.File",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Command.SaveProject", InputGesture = saveCmd.Gesture, Command = saveCmd.Command },
                    new() { IsSeparator = true },
                    new() { HeaderKey = "Command.CloseProject", Command = closeCmd.Command },
                    new() { IsSeparator = true },
                    new() { HeaderKey = "Editor.Menu.Exit", InputGesture = new Avalonia.Input.KeyGesture(Key.F4, KeyModifiers.Alt), IsEnabled = false },
                }
            },
            new()
            {
                HeaderKey = "Editor.Menu.Edit",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Editor.Menu.Undo", InputGesture = new Avalonia.Input.KeyGesture(Key.Z, KeyModifiers.Control), Command = UndoCommand },
                    new() { HeaderKey = "Editor.Menu.Redo", InputGesture = new Avalonia.Input.KeyGesture(Key.Y, KeyModifiers.Control), Command = RedoCommand },
                }
            },
            new()
            {
                HeaderKey = "Editor.Menu.Settings",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Editor.Menu.ProjectSettings", Command = ShowProjectSettingsCommand },
                    new() { HeaderKey = "Editor.Menu.EditorSettings", Command = ShowEditorSettingsCommand },
                }
            },
            new()
            {
                HeaderKey = "Editor.Menu.View",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Editor.Menu.ProjectExplorer", Command = TogglePanelCommand, CommandParameter = "project-explorer" },
                    new() { HeaderKey = "Editor.Menu.Inspector", Command = TogglePanelCommand, CommandParameter = "inspector" },
                    new() { HeaderKey = "Editor.Menu.Log", Command = TogglePanelCommand, CommandParameter = "log" },
                    new() { HeaderKey = "Editor.Menu.GamePreview", Command = TogglePanelCommand, CommandParameter = "game-preview" },
                }
            },
            new()
            {
                HeaderKey = "Editor.Menu.Window",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Editor.Menu.SaveLayout", Command = SaveLayoutCommand },
                    new() { HeaderKey = "Editor.Menu.LoadLayout", Command = LoadLayoutCommand },
                    new() { HeaderKey = "Editor.Menu.ResetLayout", Command = ResetLayoutCommand },
                }
            },
            new()
            {
                HeaderKey = "Editor.Menu.Help",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Editor.Menu.About", IsEnabled = false },
                }
            },
        };

        MenuItems.Clear();
        foreach (var item in items)
            MenuItems.Add(item);
    }
}
