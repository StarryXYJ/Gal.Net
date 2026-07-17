using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using GalNet.Core.Services;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Dock;
using GalNet.Editor.Models;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorPageViewModel : PageViewModelBase, IMenuProvider, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly EditorShortcutService _shortcutService;
    private readonly EditorDockFactory _dockFactory;
    private readonly DockLayoutSerializer _dockLayoutSerializer;
    private readonly IEditorWindowFactory _windowFactory;
    private readonly IEditorSettingsService _editorSettingsService;
    private readonly PropertyChangedEventHandler _localizationChangedHandler;

    public IEditorLocalizationService L { get; }

    [ObservableProperty]
    private string _statusText = "";

    public string ProjectName => _projectService.Current?.Name ?? "";

    public IList<MenuData> MenuItems { get; } = new AvaloniaList<MenuData>();

    public IRootDock? Layout { get; private set; }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand TogglePanelCommand { get; }

    private bool _savingLayout;
    private bool _disposed;
    private bool _layoutSavePending;
    private readonly DispatcherTimer _layoutSaveTimer;
    public ICommand SaveLayoutCommand { get; }
    public ICommand LoadLayoutCommand { get; }
    public ICommand ResetLayoutCommand { get; }

    public EditorPageViewModel(
        INavigationService navigation,
        IProjectService projectService,
        EditorShortcutService shortcutService,
        EditorDockFactory dockFactory,
        DockLayoutSerializer dockLayoutSerializer,
        IEditorWindowFactory windowFactory,
        IEditorSettingsService editorSettingsService,
        IEditorLocalizationService localization)
    {
        _projectService = projectService;
        _shortcutService = shortcutService;
        _dockFactory = dockFactory;
        _dockLayoutSerializer = dockLayoutSerializer;
        _windowFactory = windowFactory;
        _editorSettingsService = editorSettingsService;
        L = localization;

        UndoCommand = _shortcutService.GetCommand<UndoEditorCommand>().Command;
        RedoCommand = _shortcutService.GetCommand<RedoEditorCommand>().Command;

        TogglePanelCommand = new RelayCommand<string>(TogglePanel);
        SaveLayoutCommand = new AsyncRelayCommand(SaveLayoutAsync);
        LoadLayoutCommand = new RelayCommand<string>(LoadLayout);
        ResetLayoutCommand = new RelayCommand(ResetLayout);
        _layoutSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _layoutSaveTimer.Tick += OnLayoutSaveTimerTick;

        var project = _projectService.Current
            ?? throw new InvalidOperationException("EditorPageViewModel requires an open project");

        UpdateLocalizedText();
        InitializeDock();
        BuildMenuItems();
        _dockFactory.LayoutChanged += OnDockLayoutChanged;
        _projectService.CurrentChanged += OnCurrentProjectChanged;

        _localizationChangedHandler = OnLocalizationChanged;
        L.PropertyChanged += _localizationChangedHandler;
    }

    private void OnCurrentProjectChanged(GalProject? _)
    {
        OnPropertyChanged(nameof(ProjectName));
        UpdateLocalizedText();
    }

    public bool TryExecuteShortcut(KeyEventArgs args, string context = "Global") =>
        _shortcutService.TryExecute(new Avalonia.Input.KeyGesture(args.Key, args.KeyModifiers), context);


    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]")
            UpdateLocalizedText();
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
        var settings = _editorSettingsService.GetSettings();
        Layout = TryLoadLayout(settings.LastDockLayout);
        if (Layout is null && !string.IsNullOrWhiteSpace(settings.LastDockLayout))
        {
            // A previous implementation could persist the host Window back-reference.
            // Discard that malformed global value once so every subsequent start is clean.
            settings.LastDockLayout = null;
            _editorSettingsService.SaveSettings();
            StatusText = "Saved window layout was invalid and has been reset.";
        }
        if (Layout is null)
        {
            Layout = _dockFactory.CreateLayout();
            _dockFactory.InitLayout(Layout);
        }
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
        settings.DockLayouts[name] = SerializeLayout(Layout);
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
        var layout = TryLoadLayout(serialized);
        if (layout is null)
        {
            StatusText = $"Window layout '{name}' could not be loaded.";
            return;
        }
        Layout = layout;
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
            var layout = _dockLayoutSerializer.Deserialize(serialized);
            if (layout is null)
                return null;

            _dockFactory.InitLayout(layout);
            return _dockFactory.PrepareRestoredLayout(layout) ? layout : null;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to restore editor dock layout");
            return null;
        }
    }

    private void OnDockLayoutChanged()
    {
        if (Layout is null) return;
        _layoutSavePending = true;
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Start();
        RefreshViewMenuChecks();
    }

    private void OnLayoutSaveTimerTick(object? sender, EventArgs e)
    {
        _layoutSaveTimer.Stop();
        PersistLayoutNow();
    }

    private void PersistLayoutCore()
    {
        if (_savingLayout || Layout is null) return;
        try
        {
            _savingLayout = true;
            var serialized = SerializeLayout(Layout);
            _editorSettingsService.GetSettings().LastDockLayout = serialized;
            _editorSettingsService.SaveSettings();
        }
        catch (Exception exception)
        {
            StatusText = "Failed to save window layout.";
            Log.Error(exception, "Failed to persist editor dock layout");
        }
        finally { _savingLayout = false; }
    }

    /// <summary>Called after splitter interaction; persistence is debounced to avoid disk writes during dragging.</summary>
    public void PersistLayout() => OnDockLayoutChanged();

    /// <summary>Flushes a pending layout change. Called during application shutdown.</summary>
    public void PersistLayoutNow()
    {
        _layoutSaveTimer.Stop();
        if (!_layoutSavePending && Layout is null) return;
        _layoutSavePending = false;
        PersistLayoutCore();
    }

    private string SerializeLayout(IRootDock layout) => _dockLayoutSerializer.Serialize(layout);

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
        var closeCmd = _shortcutService.GetCommand<CloseProjectCommand>();
        var saveCmd = _shortcutService.GetCommand<SaveProjectCommand>();

        var items = new AvaloniaList<MenuData>
        {
            new()
            {
                HeaderKey = "Editor.Menu.File",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Command.SaveProject", Command = saveCmd.Command, InputGesture = saveCmd.Gesture },
                    new() { HeaderKey = "Command.CloseProject", Command = closeCmd.Command, InputGesture = closeCmd.Gesture },
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        PersistLayoutNow();
        _layoutSaveTimer.Tick -= OnLayoutSaveTimerTick;
        _dockFactory.LayoutChanged -= OnDockLayoutChanged;
        _projectService.CurrentChanged -= OnCurrentProjectChanged;
        L.PropertyChanged -= _localizationChangedHandler;
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
