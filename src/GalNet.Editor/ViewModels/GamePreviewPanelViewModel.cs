using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Screen.Flow;
using GalNet.Control.Screen.Game;
using GalNet.Control.Screen.Host;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Control.Services;
using GalNet.Control.UI;
using GalNet.Core.Assets;
using Serilog;
using Serilog.Context;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.ViewModels;

public partial class GamePreviewPanelViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private readonly GalNet.Core.Services.INavigationService _navigation;
    private readonly IGameFlowFactory _gameFlowFactory;
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly IProjectService _projectService;
    private readonly EditorVariableService _variableService;
    private readonly IVariableDefinitionService _variableDefinitions;
    private readonly IEditorDocumentService _documentService;
    private readonly IAssetManager _assets;

    private IGameRuntime? _runtime;
    private GameRunViewModel? _activeRun;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IDisposable? _projectClosingRegistration;
    private bool _disposed;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isGameStarted;

    [ObservableProperty]
    private GamePageHostViewModel? _pageHostVm;

    [ObservableProperty]
    private VariableListEditorViewModel? _playerVariables;

    [ObservableProperty]
    private VariableListEditorViewModel? _saveVariables;

    public int DesignWidth => _projectService.Current?.Settings.DefaultWidth ?? 1920;
    public int DesignHeight => _projectService.Current?.Settings.DefaultHeight ?? 1080;

    public ObservableCollection<string> OutputLines { get; } = [];

    public GamePreviewPanelViewModel(
        GalNet.Core.Services.INavigationService navigation,
        IGameFlowFactory gameFlowFactory,
        EditorWorkspaceViewModel workspace,
        IProjectService projectService,
        EditorVariableService variableService,
        IVariableDefinitionService variableDefinitions,
        IEditorDocumentService documentService, IAssetManager assets)
    {
        _navigation = navigation;
        _gameFlowFactory = gameFlowFactory;
        _workspace = workspace;
        _projectService = projectService;
        _variableService = variableService;
        _variableDefinitions = variableDefinitions;
        _documentService = documentService;
        _assets = assets;
        _workspace.ActivePreview = this;
        _projectClosingRegistration = _projectService.Current?.RegisterClosingCallback(DisposePreviewForProjectCloseAsync);

        _projectService.CurrentChanged += OnProjectChanged;
        _variableService.VariableChanged += OnVariableServiceChanged;

        ReloadEditors();
        _ = RestartPreviewAsync();
    }

    [RelayCommand]
    private async Task RestartPreviewAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_disposed || _projectService.Current is not { } project)
            {
                StatusText = "No project";
                return;
            }

            StatusText = "Restarting...";
            await StopPreviewAsync();
            if (_disposed || !ReferenceEquals(_projectService.Current, project))
                return;

            using var gameLogContext = LogContext.PushProperty("LogChannel", "Game");
            var options = new GameFlowOptions
            {
                Title = project.Name,
                GameContentProvider = project.Services.GetRequiredService<IGameContentProvider>(),
                Ui = project.UiProject.Current,
                AssetManager = _assets,
                SaveService = new FileSaveService(Path.Combine(project.EditorStateDirectory, "player"), project.Settings.SaveSlotCount),
                ProgressService = new FileGameProgressService(Path.Combine(project.EditorStateDirectory, "player")),
                VariableService = _variableService,
                RuntimeCreated = OnRuntimeCreated,
                GameStarted = OnGameStarted,
                GameEnded = OnGameEnded,
                GameFailed = OnGameFailed,
                RunCreated = run => _activeRun = run
            };

            PageHostVm = _gameFlowFactory.CreatePageHost(_navigation, options);
            ReloadEditors();
            OutputLines.Insert(0, $"Preview restarted at {DateTime.Now:T}");
            StatusText = "Ready";
        }
        finally { _lifecycleGate.Release(); }
    }

    public Task RestartAsync() => RestartPreviewAsync();

    public async Task ResetPlayerAsync()
    {
        _variableService.ResetAll();
        if (_projectService.Current is { } project)
        {
            var playerDirectory = Path.Combine(project.EditorStateDirectory, "player");
            var savesDirectory = Path.Combine(playerDirectory, "saves");
            if (Directory.Exists(savesDirectory)) Directory.Delete(savesDirectory, true);
            var progressPath = Path.Combine(playerDirectory, "progress.json");
            if (File.Exists(progressPath)) File.Delete(progressPath);
        }
        await RestartPreviewAsync();
    }

    private void OnVariableServiceChanged(VariableScope scope, string name, Variable variable)
    {
        Log.Debug(
            "Preview variable synchronized: {Scope}.{VariableName} = {VariableValue} ({VariableType})",
            scope, name, GetRawValue(variable), variable.Type);

        // Update runtime as well when editor modifies player variables. VariableStore
        // ignores equal values, so runtime-originated notifications are not echoed.
        if (scope == VariableScope.Player)
            _runtime?.SetVariable(name, GetRawValue(variable));

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;

            var editor = scope == VariableScope.Player ? PlayerVariables : SaveVariables;
            editor?.UpdateCurrentValue(name, variable);
        });
    }

    private void ReloadEditors()
    {
        if (_projectService.Current is null)
            return;

        PlayerVariables = new VariableListEditorViewModel(
            _variableDefinitions.GetDefinitions(VariableScope.Player),
            VariableScope.Player,
            showCurrentValue: true,
            allowCurrentEditing: true,
            IsNameAvailable,
            ResolveAvailableName,
            name => _variableService.GetSnapshot(VariableScope.Player).TryGetValue(name, out var v) ? CloneVariable(v, name) : null,
            (name, value) =>
            {
                _variableService.GetSnapshot(VariableScope.Player).TryGetValue(name, out var existing);

                var v = existing is not null ? CloneVariable(existing, name) : new Variable { Name = name };
                v.SetValue(value);
                _variableService.NotifyVariableChanged(VariableScope.Player, name, v);
            },
            name => _variableService.RemoveRuntimeVariable(VariableScope.Player, name),
            RenamePlayerVariable,
            PersistVariableDefinitions,
            OnNameConflict);

        SaveVariables = new VariableListEditorViewModel(
            _variableDefinitions.GetDefinitions(VariableScope.Save),
            VariableScope.Save,
            showCurrentValue: IsGameStarted,
            allowCurrentEditing: true,
            IsNameAvailable,
            ResolveAvailableName,
            name => _variableService.GetSnapshot(VariableScope.Save).TryGetValue(name, out var v) ? CloneVariable(v, name) : null,
            (name, value) =>
            {
                if (_runtime is null) return;
                _runtime.SetVariable(name, value);
            },
            name => _variableService.RemoveRuntimeVariable(VariableScope.Save, name),
            RenameSaveVariable,
            PersistVariableDefinitions);

        OnPropertyChanged(nameof(PlayerVariables));
        OnPropertyChanged(nameof(SaveVariables));
    }

    private bool IsNameAvailable(string name, VariableScope scope)
    {
        return _variableDefinitions.IsNameAvailable(name, scope);
    }

    private string ResolveAvailableName(string name, VariableScope scope)
    {
        var sanitized = VariableNameRules.Sanitize(name, $"var_{scope.ToString().ToLowerInvariant()}");
        if (_variableDefinitions.IsNameAvailable(sanitized, scope))
            return sanitized;

        var suffix = 1;
        var candidate = sanitized;
        while (!_variableDefinitions.IsNameAvailable(candidate, scope))
            candidate = $"{sanitized}_{suffix++}";

        return candidate;
    }

    private void RenamePlayerVariable(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        _variableService.RenameRuntimeVariable(VariableScope.Player, oldName, newName);
        if (_variableService.GetSnapshot(VariableScope.Player).TryGetValue(newName, out var variable))
            _runtime?.SetVariable(newName, GetRawValue(variable));
    }

    private void RenameSaveVariable(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        _variableService.RenameRuntimeVariable(VariableScope.Save, oldName, newName);
    }

    private void PersistVariableDefinitions()
    {
        _variableDefinitions.ResetFromDocument();
        _documentService.MarkDirty();
        if (_projectService.Current is { } project)
            project.IsDirty = true;
    }

    /// <summary>Event raised when a variable name conflict is detected. The view shows a dialog.</summary>
    public event Action<string>? NameConflictRequested;

    private void OnNameConflict(string name)
    {
        NameConflictRequested?.Invoke(name);
    }

    private void OnRuntimeCreated(IGameRuntime runtime)
    {
        _runtime = runtime;
    }

    private void OnGameStarted()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            IsGameStarted = true;
            StatusText = "Running";
        });
    }

    private void OnGameEnded()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _runtime = null;
            _activeRun = null;
            IsGameStarted = false;
            StatusText = "Ready";
        });
    }

    private void OnGameFailed(Exception exception)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _runtime = null;
            _activeRun = null;
            IsGameStarted = false;
            StatusText = $"Preview failed: {exception.Message}";
        });
    }

    partial void OnIsGameStartedChanged(bool value)
    {
        SaveVariables?.SetCurrentValueVisibility(value);
    }

    private async Task StopPreviewAsync()
    {
        IsGameStarted = false;
        _runtime = null;
        var run = _activeRun;
        _activeRun = null;
        if (run is not null)
            await run.DisposeAsync();
        ReloadEditors();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposePreviewForProjectCloseAsync();
    }

    /// <summary>
    /// Dock.Model disposes document contexts synchronously. Start the same idempotent
    /// cleanup path without blocking the UI thread that owns the document close.
    /// </summary>
    public void Dispose() => _ = DisposeAsync();

    private async Task DisposePreviewForProjectCloseAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            _projectClosingRegistration?.Dispose();
            _projectClosingRegistration = null;
            _projectService.CurrentChanged -= OnProjectChanged;
            _variableService.VariableChanged -= OnVariableServiceChanged;
            if (ReferenceEquals(_workspace.ActivePreview, this)) _workspace.ActivePreview = null;
            await StopPreviewAsync();
        }
        finally { _lifecycleGate.Release(); }
    }

    private void OnProjectChanged(GalProject? project)
    {
        OnPropertyChanged(nameof(DesignWidth));
        OnPropertyChanged(nameof(DesignHeight));
        if (!_disposed)
            _ = StopPreviewForProjectChangeAsync();
    }

    private async Task StopPreviewForProjectChangeAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (!_disposed)
                await StopPreviewAsync();
        }
        finally { _lifecycleGate.Release(); }
    }

    private static Variable CloneVariable(Variable variable, string name)
    {
        var clone = new Variable { Name = name };
        clone.SetValue(variable.Type switch
        {
            VariableType.Bool => variable.AsBool(),
            VariableType.Int => variable.AsInt(),
            VariableType.Float => variable.AsFloat(),
            _ => variable.AsString()
        });
        return clone;
    }

    private static object GetRawValue(Variable variable) => variable.Type switch
    {
        VariableType.Bool => variable.AsBool(),
        VariableType.Int => variable.AsInt(),
        VariableType.Float => variable.AsFloat(),
        _ => variable.AsString()
    };
}
