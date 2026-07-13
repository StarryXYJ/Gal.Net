using System;
using System.Collections.Generic;
using System.Linq;
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
using Dock.Serializer.SystemTextJson;
using GalNet.Core.Services;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Dock;
using GalNet.Editor.Models;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

public partial class EditorPageViewModel : PageViewModelBase, IMenuProvider
{
    private readonly IProjectService _projectService;
    private readonly CommandService _commandService;
    private readonly EditorDockFactory _dockFactory;
    private readonly IEditorWindowFactory _windowFactory;
    private readonly IEditorSettingsService _editorSettingsService;

    public IEditorLocalizationService L { get; }

    [ObservableProperty]
    private string _statusText = "";

    public string ProjectName => _projectService.Current?.Name ?? "";

    public IList<MenuData> MenuItems { get; } = new AvaloniaList<MenuData>();

    public IRootDock? Layout { get; private set; }

    public ICommand UndoCommand { get; } = new RelayCommand(() => { }, () => false);
    public ICommand RedoCommand { get; } = new RelayCommand(() => { }, () => false);
    public ICommand TogglePanelCommand { get; }

    private readonly DockSerializer _dockSerializer = new();
    private bool _savingLayout;
    public ICommand SaveLayoutCommand { get; }
    public ICommand LoadLayoutCommand { get; }
    public ICommand ResetLayoutCommand { get; }

    public EditorPageViewModel(
        INavigationService navigation,
        IProjectService projectService,
        CommandService commandService,
        EditorDockFactory dockFactory,
        IEditorWindowFactory windowFactory,
        IEditorSettingsService editorSettingsService,
        IEditorLocalizationService localization)
    {
        _projectService = projectService;
        _commandService = commandService;
        _dockFactory = dockFactory;
        _windowFactory = windowFactory;
        _editorSettingsService = editorSettingsService;
        L = localization;

        TogglePanelCommand = new RelayCommand<string>(TogglePanel);
        SaveLayoutCommand = new AsyncRelayCommand(SaveLayoutAsync);
        LoadLayoutCommand = new RelayCommand<string>(LoadLayout);
        ResetLayoutCommand = new RelayCommand(ResetLayout);

        var project = _projectService.Current
            ?? throw new InvalidOperationException("EditorPageViewModel requires an open project");

        UpdateLocalizedText();
        InitializeDock();
        BuildMenuItems();
        _dockFactory.LayoutChanged += OnDockLayoutChanged;

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
        var saved = _editorSettingsService.GetSettings().LastDockLayout;
        Layout = TryLoadLayout(saved) ?? _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);
        OnPropertyChanged(nameof(Layout));
    }

    private void TogglePanel(string? panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId))
            return;
        var panel = _dockFactory.ViewMenuPanels.FirstOrDefault(item => item.PanelId == panelId);
        if (panel is null) return;
        if (panel.IsGlobal) _dockFactory.ToggleGlobalPanel(panelId);
        else _dockFactory.OpenPanel(panelId);
    }

    private async Task SaveLayoutAsync()
    {
        if (Layout is null)
            return;
        var name = await RequestLayoutNameAsync();
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "Default is reserved.";
            return;
        }
        var settings = _editorSettingsService.GetSettings();
        settings.DockLayouts[name] = _dockSerializer.Serialize(Layout);
        _editorSettingsService.SaveSettings();
        StatusText = $"Window layout '{name}' saved.";
        BuildMenuItems();
    }

    private void LoadLayout(string? name)
    {
        if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
        {
            ResetLayout();
            return;
        }
        var settings = _editorSettingsService.GetSettings();
        if (string.IsNullOrWhiteSpace(name) || !settings.DockLayouts.TryGetValue(name, out var serialized))
        {
            StatusText = "No saved window layout.";
            return;
        }
        Layout = TryLoadLayout(serialized) ?? _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);
        OnPropertyChanged(nameof(Layout));
        StatusText = $"Window layout '{name}' loaded.";
    }

    private void ResetLayout()
    {
        Layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);
        OnPropertyChanged(nameof(Layout));
        StatusText = "Default window layout restored.";
    }

    private void DeleteLayout(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)) return;
        var settings = _editorSettingsService.GetSettings();
        if (!settings.DockLayouts.Remove(name)) return;
        _editorSettingsService.SaveSettings();
        StatusText = $"Window layout '{name}' deleted.";
        BuildMenuItems();
    }

    private IRootDock? TryLoadLayout(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized)) return null;
        try
        {
            var layout = _dockSerializer.Deserialize<IRootDock>(serialized);
            return layout is not null && _dockFactory.PrepareRestoredLayout(layout) ? layout : null;
        }
        catch { return null; }
    }

    private void OnDockLayoutChanged()
    {
        if (_savingLayout || Layout is null) return;
        try
        {
            _savingLayout = true;
            _editorSettingsService.GetSettings().LastDockLayout = _dockSerializer.Serialize(Layout);
            _editorSettingsService.SaveSettings();
            RefreshViewMenuChecks();
        }
        catch { /* Layout persistence must not interrupt editing. */ }
        finally { _savingLayout = false; }
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
        var closeCmd = _commandService.GetCommand<CloseProjectCommand>();

        var items = new AvaloniaList<MenuData>
        {
            new()
            {
                HeaderKey = "Editor.Menu.File",
                Children = new AvaloniaList<MenuData>
                {
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
                Children = BuildViewMenuItems()
            },
            new()
            {
                HeaderKey = "Editor.Menu.Window",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Editor.Menu.SaveLayout", Command = SaveLayoutCommand },
                    new() { HeaderKey = "Editor.Menu.LoadLayout", Children = BuildLoadLayoutItems() },
                    new() { HeaderKey = "Editor.Menu.DeleteLayout", Children = BuildDeleteLayoutItems() },
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

    private IList<MenuData> BuildViewMenuItems() => new AvaloniaList<MenuData>(
        _dockFactory.ViewMenuPanels.Select(panel => new MenuData
        {
            HeaderKey = panel.TitleKey,
            Command = TogglePanelCommand,
            CommandParameter = panel.PanelId,
            IsCheckable = panel.IsGlobal,
            IsChecked = panel.IsGlobal && _dockFactory.HasGlobalPanel(panel.PanelId)
        }));

    private IList<MenuData> BuildLoadLayoutItems()
    {
        var items = new AvaloniaList<MenuData> { new() { Header = "Default", Command = LoadLayoutCommand, CommandParameter = "Default" } };
        foreach (var name in _editorSettingsService.GetSettings().DockLayouts.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            items.Add(new MenuData { Header = name, Command = LoadLayoutCommand, CommandParameter = name });
        return items;
    }

    private IList<MenuData> BuildDeleteLayoutItems() => new AvaloniaList<MenuData>(
        _editorSettingsService.GetSettings().DockLayouts.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new MenuData { Header = name, Command = new RelayCommand<string>(DeleteLayout), CommandParameter = name }));

    private void RefreshViewMenuChecks()
    {
        var view = MenuItems.FirstOrDefault(item => item.HeaderKey == "Editor.Menu.View");
        if (view?.Children is null) return;
        foreach (var item in view.Children.Where(item => item.IsCheckable && item.CommandParameter is string))
        {
            if (item.CommandParameter is string panelId)
                item.IsChecked = _dockFactory.HasGlobalPanel(panelId);
        }
    }

    private async Task<string?> RequestLayoutNameAsync()
    {
        var owner = GetMainWindow();
        if (owner is null) return null;
        var input = new TextBox { MinWidth = 260, PlaceholderText = "Layout name" };
        var dialog = new Window
        {
            Title = "Save Window Layout",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                input,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        new Button { Content = "Cancel", Command = new RelayCommand(() => dialog.Close(null)) },
                        new Button { Content = "Save", Command = new RelayCommand(() => dialog.Close(input.Text)) }
                    }
                }
            }
        };
        return await dialog.ShowDialog<string?>(owner);
    }
}
