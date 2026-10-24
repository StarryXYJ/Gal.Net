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
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly EditorHistories _histories;
    private readonly IProjectContentSession _content;

    public IEditorLocalizationService L { get; }

    [ObservableProperty]
    private string _statusText = "";

    public string ProjectName => _projectService.Current?.Name ?? "";
    private EntryEditSession? Edits => _projectService.Current?.Services.GetService<EntryEditSession>();
    public string SaveStatus => L[_projectService.Current is null ? "Editor.Status.Ready" : _content.RepairRequired ? "Status.Repair" : _content.LastError is not null ? "Uiux.Save.Failed"
        : _content.HasEditsSincePublication ? "Uiux.Save.Unsaved" : _content.IsPublishing ? "Uiux.Save.Saving" : Edits?.PendingCount > 0 ? "Uiux.Save.Drafts" : _content.IsDirty ? "Uiux.Save.Unsaved" : "Uiux.Save.Saved"];
    public string DraftCount => Edits?.PendingCount is > 0 and var count ? L.Format("Uiux.Save.PendingCount", count) : "";
    public ProblemsPanelViewModel? Problems => _projectService.Current?.Services.GetService<ProblemsPanelViewModel>();
    public ICommand ShowProblemsCommand => new RelayCommand(() => _dockFactory.OpenPanel(EditorDockPanelIds.Problems));

    public IList<MenuData> MenuItems { get; } = new AvaloniaList<MenuData>();

    public IRootDock? Layout { get; private set; }

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand TogglePanelCommand { get; }

    private EditorText? _layoutStatus;
    private void SetLayoutStatus(EditorText text) { _layoutStatus = text; StatusText = text.Render(L); }
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
        IEditorLocalizationService localization,
        EditorHistories histories,
        IProjectContentSession content)
    {
        _projectService = projectService;
        _histories = histories;
        _content = content;
        _content.Changed += OnContentChanged;
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
        _histories.Changed += OnHistoryChanged;
        OnHistoryChanged();
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

    private void OnHistoryChanged()
    {
        foreach (var item in MenuItems.SelectMany(menu => menu.Children ?? []))
        {
            if (ReferenceEquals(item.Command, UndoCommand)) item.Header = L["Editor.Menu.Undo"] + (_histories.Content.UndoText is { } undo ? ": " + undo.Render(L) : "");
            if (ReferenceEquals(item.Command, RedoCommand)) item.Header = L["Editor.Menu.Redo"] + (_histories.Content.RedoText is { } redo ? ": " + redo.Render(L) : "");
        }
        OnContentChanged();
    }

    private void OnContentChanged()
    {
        OnPropertyChanged(nameof(SaveStatus)); OnPropertyChanged(nameof(DraftCount));
        OnPropertyChanged(nameof(ProjectName));
        UpdateLocalizedText();
        if (_content.RepairRequired) StatusText = L["Status.Repair"];
        else if (_content.LastError is { } error) StatusText = L.Format("Status.SaveFailed", EditorText.Error(error).Render(L));
        else if (_content.LastRecoveryError is { } recovery) StatusText = L.Format("Status.RecoveryFailed", EditorText.Error(recovery).Render(L));
        else if (_layoutStatus is not null) StatusText = _layoutStatus.Render(L);
    }

    public bool TryExecuteShortcut(KeyEventArgs args, string context = "Global") =>
        _shortcutService.TryExecute(new Avalonia.Input.KeyGesture(args.Key, args.KeyModifiers), context);


    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]")
            OnHistoryChanged();
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
            SetLayoutStatus(new("Status.LayoutReset"));
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
            SetLayoutStatus(new("Layout.Reserved"));
            return;
        }
        var settings = _editorSettingsService.GetSettings();
        settings.DockLayouts[name] = SerializeLayout(Layout);
        _editorSettingsService.SaveSettings();
        SetLayoutStatus(new("Layout.Saved", name));
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
            SetLayoutStatus(new("Layout.None"));
            return;
        }
        var layout = TryLoadLayout(serialized);
        if (layout is null)
        {
            SetLayoutStatus(new("Layout.LoadFailed", name));
            return;
        }
        Layout = layout;
        OnPropertyChanged(nameof(Layout));
        SetLayoutStatus(new("Layout.Loaded", name));
    }

    private void ResetLayout()
    {
        Layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);
        OnPropertyChanged(nameof(Layout));
        SetLayoutStatus(new("Layout.Reset"));
    }

    private void DeleteLayout(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)) return;
        var settings = _editorSettingsService.GetSettings();
        if (!settings.DockLayouts.Remove(name)) return;
        _editorSettingsService.SaveSettings();
        SetLayoutStatus(new("Layout.Deleted", name));
        BuildMenuItems();
    }

    private IRootDock? TryLoadLayout(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized)) return null;
        try
        {
            var layout = DockLayoutSerializer.Deserialize(serialized);
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
        if (_savingLayout || Layout is null || _dockFactory.IsMaximized) return;
        try
        {
            _savingLayout = true;
            var serialized = SerializeLayout(Layout);
            _editorSettingsService.GetSettings().LastDockLayout = serialized;
            _editorSettingsService.SaveSettings();
        }
        catch (Exception exception)
        {
            SetLayoutStatus(new("Layout.SaveFailed"));
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

    [RelayCommand]
    private async Task ShowExportAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null) return;
        await _windowFactory.CreateExportWindow().ShowDialog(mainWindow);
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
        ICommand DebugAction(Func<ExternalDebugPanelViewModel, Task> action) => new AsyncRelayCommand(async () =>
        {
            if (_projectService.Current is not { } project) return;
            if (!_dockFactory.HasGlobalPanel(EditorDockPanelIds.ExternalDebug)) TogglePanel(EditorDockPanelIds.ExternalDebug);
            await action(project.Services.GetRequiredService<ExternalDebugPanelViewModel>());
        });

        var items = new AvaloniaList<MenuData>
        {
            new()
            {
                HeaderKey = "Editor.Menu.File",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Command.SaveProject", Command = saveCmd.Command, InputGesture = saveCmd.Gesture },
                    new() { HeaderKey = "Editor.Menu.Export", Command = ShowExportCommand },
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
                    new() { HeaderKey = "Uiux.Layout.Writing", Command = new RelayCommand(() => _dockFactory.ApplyWorkspacePreset("Writing")) },
                    new() { HeaderKey = "Uiux.Layout.Animation", Command = new RelayCommand(() => _dockFactory.ApplyWorkspacePreset("Animation")) },
                    new() { HeaderKey = "Uiux.Layout.Debug", Command = new RelayCommand(() => _dockFactory.ApplyWorkspacePreset("Debug")) },
                    new() { HeaderKey = "Uiux.Layout.Maximize", Command = new RelayCommand(_dockFactory.ToggleMaximizeActive) },
                    new() { HeaderKey = "Editor.Menu.SaveLayout", Command = SaveLayoutCommand },
                    new() { HeaderKey = "Editor.Menu.LoadLayout", Children = BuildLoadLayoutItems() },
                    new() { HeaderKey = "Editor.Menu.DeleteLayout", Children = BuildDeleteLayoutItems() },
                }
            },
            new()
            {
                HeaderKey = "Debug.Menu",
                Children = new AvaloniaList<MenuData>
                {
                    new() { HeaderKey = "Debug.Launch", Command = DebugAction(vm => vm.LaunchCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.Wait", Command = DebugAction(vm => vm.WaitForApplicationCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.Start", Command = DebugAction(vm => vm.StartGameCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.Stop", Command = DebugAction(vm => vm.StopGameCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.GroupEnd", Command = DebugAction(vm => vm.GroupEndCommand.ExecuteAsync(null)) },
                    new() { HeaderKey = "Debug.Panel", Command = TogglePanelCommand, CommandParameter = EditorDockPanelIds.ExternalDebug }
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
        _histories.Changed -= OnHistoryChanged;
        _content.Changed -= OnContentChanged;
        L.PropertyChanged -= _localizationChangedHandler;
        GC.SuppressFinalize(this);
    }

    private AvaloniaList<MenuData> BuildViewMenuItems() => new(
        _dockFactory.ViewMenuPanels.Select(panel => new MenuData
        {
            HeaderKey = panel.TitleKey,
            Command = TogglePanelCommand,
            CommandParameter = panel.PanelId,
            IsCheckable = panel.IsGlobal,
            IsChecked = panel.IsGlobal && _dockFactory.HasGlobalPanel(panel.PanelId)
        }));

    private AvaloniaList<MenuData> BuildLoadLayoutItems()
    {
        var items = new AvaloniaList<MenuData> { new() { Header = "Default", Command = LoadLayoutCommand, CommandParameter = "Default" } };
        foreach (var name in _editorSettingsService.GetSettings().DockLayouts.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            items.Add(new MenuData { Header = name, Command = LoadLayoutCommand, CommandParameter = name });
        return items;
    }

    private AvaloniaList<MenuData> BuildDeleteLayoutItems() => new(
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
        var input = new TextBox { MinWidth = 260, PlaceholderText = L["Layout.Name"] };
        var dialog = new Window
        {
            Title = L["Editor.Menu.SaveLayout"],
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
                        new Button { Content = L["Common.Cancel"], Command = new RelayCommand(() => dialog.Close(null)) },
                        new Button { Content = L["Common.Save"], Command = new RelayCommand(() => dialog.Close(input.Text)) }
                    }
                }
            }
        };
        var bindings = new List<IDisposable> { LocalizedBindings.Bind(dialog, Window.TitleProperty, L, "Editor.Menu.SaveLayout"), LocalizedBindings.Bind(input, TextBox.PlaceholderTextProperty, L, "Layout.Name") };
        foreach (var button in ((StackPanel)dialog.Content!).Children.OfType<StackPanel>().SelectMany(panel => panel.Children).OfType<Button>())
            bindings.Add(LocalizedBindings.Bind(button, ContentControl.ContentProperty, L, button.Content?.ToString() == L["Common.Cancel"] ? "Common.Cancel" : "Common.Save"));
        dialog.Closed += (_, _) => { foreach (var binding in bindings) binding.Dispose(); };
        return await dialog.ShowDialog<string?>(owner);
    }
}
