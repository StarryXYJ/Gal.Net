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
using GalNet.Editor.Abstraction.Settings;
using GalNet.Editor.Commands;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shell;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorPageViewModel : PageViewModelBase, IMenuProvider, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly EditorShortcutService _shortcutService;
    private readonly EditorDockFactory _dockFactory;
    private readonly EditorLayoutSession _layouts;
    private readonly IEditorWindowFactory _windowFactory;
    private readonly PropertyChangedEventHandler _localizationChangedHandler;
    private readonly EditorHistories _histories;
    private readonly IProjectContentSession _content;
    private readonly ProjectExternalChanges? _externalChanges;
    public bool HasExternalIssue => _externalChanges?.HasIssue == true;
    public bool CanReloadExternal => _externalChanges?.CanReload == true;
    public string ExternalDetails => _externalChanges?.Details ?? "";
    public string ExternalStatus => L[_externalChanges?.State switch
    {
        ExternalProjectState.LocalChanges => "External.LocalChanges",
        ExternalProjectState.Publishing => "External.Publishing",
        ExternalProjectState.ContractsChanged => "External.ContractsChanged",
        ExternalProjectState.Invalid => "External.Invalid",
        ExternalProjectState.Reloaded => "External.Reloaded",
        _ => "Editor.Status.Ready"
    }];
    public ICommand RetryExternalChangesCommand => new AsyncRelayCommand(async () => { if (_externalChanges is not null) await _externalChanges.CheckNowAsync(); });
    public ICommand ReloadExternalChangesCommand => new AsyncRelayCommand(async () =>
    {
        if (_externalChanges is null) return;
        if (!_content.IsDirty || await ProjectReloadPrompt.ConfirmAsync(L)) await _externalChanges.CheckNowAsync(discardLocalChanges: true);
    });

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

    public IRootDock? Layout => _layouts.Layout;

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand TogglePanelCommand { get; }

    private bool _disposed;
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
        IProjectContentSession content, ProjectExternalChanges? externalChanges = null)
    {
        _projectService = projectService;
        _histories = histories;
        _content = content;
        _externalChanges = externalChanges;
        _content.Changed += OnContentChanged;
        _shortcutService = shortcutService;
        _dockFactory = dockFactory;
        _layouts = new(dockFactory, dockLayoutSerializer, editorSettingsService);
        _layouts.Changed += OnLayoutChanged;
        _layouts.NamesChanged += BuildMenuItems;
        _windowFactory = windowFactory;
        L = localization;

        UndoCommand = _shortcutService.GetCommand<UndoEditorCommand>().Command;
        RedoCommand = _shortcutService.GetCommand<RedoEditorCommand>().Command;

        TogglePanelCommand = new RelayCommand<string>(TogglePanel);
        SaveLayoutCommand = new AsyncRelayCommand(() => _layouts.SaveAsync(() => _windowFactory.RequestLayoutNameAsync(L)));
        LoadLayoutCommand = new RelayCommand<string>(_layouts.Load);
        ResetLayoutCommand = new RelayCommand(_layouts.Reset);

        var project = _projectService.Current
            ?? throw new InvalidOperationException("EditorPageViewModel requires an open project");
        if (_externalChanges is not null) { _externalChanges.Changed += OnExternalChanged; _externalChanges.Start(); }

        UpdateLocalizedText();
        _layouts.Initialize();
        BuildMenuItems();
        _histories.Changed += OnHistoryChanged;
        OnHistoryChanged();
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
        else if (_externalChanges?.State == ExternalProjectState.Reloaded) StatusText = ExternalStatus;
        else if (_layouts.Status is { } layoutStatus) StatusText = layoutStatus.Render(L);
    }

    private void OnExternalChanged()
    {
        OnPropertyChanged(nameof(HasExternalIssue)); OnPropertyChanged(nameof(CanReloadExternal)); OnPropertyChanged(nameof(ExternalStatus)); OnPropertyChanged(nameof(ExternalDetails));
        OnContentChanged();
    }

    public bool TryExecuteShortcut(KeyEventArgs args, string context = "Global") =>
        _shortcutService.TryExecute(new Avalonia.Input.KeyGesture(args.Key, args.KeyModifiers), context);


    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]")
        { OnHistoryChanged(); OnExternalChanged(); }
    }

    private void UpdateLocalizedText()
    {
        var project = _projectService.Current;
        Title = project is null ? L["App.Title"] : $"{L["App.Title"]} - {project.Name}";
        StatusText = project is null
            ? L["Editor.Status.Ready"]
            : L.Format("Editor.Status.Project", project.Name, project.RootPath);
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
        var items = new EditorMenuBuilder(_shortcutService, _dockFactory, _layouts, _projectService,
            new(UndoCommand, RedoCommand, TogglePanelCommand, SaveLayoutCommand, LoadLayoutCommand,
                ShowExportCommand, ShowProjectSettingsCommand, ShowEditorSettingsCommand)).Build();
        MenuItems.Clear();
        foreach (var item in items) MenuItems.Add(item);
        OnHistoryChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _layouts.Changed -= OnLayoutChanged;
        _layouts.NamesChanged -= BuildMenuItems;
        _layouts.Dispose();
        _projectService.CurrentChanged -= OnCurrentProjectChanged;
        _histories.Changed -= OnHistoryChanged;
        _content.Changed -= OnContentChanged;
        if (_externalChanges is not null) _externalChanges.Changed -= OnExternalChanged;
        L.PropertyChanged -= _localizationChangedHandler;
        GC.SuppressFinalize(this);
    }

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

    private void OnLayoutChanged()
    {
        OnPropertyChanged(nameof(Layout));
        RefreshViewMenuChecks();
        OnContentChanged();
    }
    public void PersistLayout() => _layouts.PersistLayout();
    public void PersistLayoutNow() => _layouts.PersistLayoutNow();
}
