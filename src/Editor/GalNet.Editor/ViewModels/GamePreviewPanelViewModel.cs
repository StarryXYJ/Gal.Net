using GalNet.Contracts.Builtins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Runtime;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Runtime.Content;
using GalNet.Runtime.Runtime;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Context;

namespace GalNet.Editor.ViewModels;

public partial class GamePreviewPanelViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly IProjectService _projectService;
    private readonly EditorVariableService _variableService;
    private readonly IVariableDefinitionService _variableDefinitions;
    private readonly IGameContentProvider _contentProvider;
    private readonly IAssetManager _assets;

    private IGameRuntime? _runtime;
    private EditorPreviewHost? _previewHost;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IDisposable? _projectClosingRegistration;
    private bool _disposed;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isGameStarted;

    private Control? _previewShell;
    public Control? PreviewShell
    {
        get => _previewShell;
        private set => SetProperty(ref _previewShell, value);
    }

    [ObservableProperty]
    private VariableListEditorViewModel? _playerVariables;

    [ObservableProperty]
    private VariableListEditorViewModel? _saveVariables;

    public int DesignWidth => _projectService.Current?.Configuration.ReadDefaultOptions().DefaultWidth ?? 1920;
    public int DesignHeight => _projectService.Current?.Configuration.ReadDefaultOptions().DefaultHeight ?? 1080;

    public ObservableCollection<string> OutputLines { get; } = [];

    public GamePreviewPanelViewModel(
        EditorWorkspaceViewModel workspace,
        IProjectService projectService,
        EditorVariableService variableService,
        IVariableDefinitionService variableDefinitions,
        IGameContentProvider contentProvider,
        IAssetManager assets)
    {
        _workspace = workspace;
        _projectService = projectService;
        _variableService = variableService;
        _variableDefinitions = variableDefinitions;
        _contentProvider = contentProvider;
        _assets = assets;
        _workspace.ActivePreview = this;
        _projectClosingRegistration = _projectService.Current?.RegisterClosingCallback(DisposePreviewForProjectCloseAsync);

        _projectService.CurrentChanged += OnProjectChanged;
        _variableService.VariableChanged += OnVariableServiceChanged;
        _variableDefinitions.DefinitionsChanged += OnDefinitionsChanged;

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
            var profileDirectory = Path.Combine(project.EditorStateDirectory, "player");
            var context = new EditorPreviewContext(
                project.Name,
                project.Services.GetRequiredService<IEntryCatalog>(),
                _assets,
                _contentProvider,
                _variableService,
                new FileSaveService(profileDirectory, project.Configuration.ReadDefaultOptions().SaveSlotCount),
                new FileGameProgressService(profileDirectory),
                OnRuntimeCreated,
                OnGameStarted,
                OnGameEnded,
                OnGameFailed);
            var host = new EditorPreviewHost(context);
            _previewHost = host;
            PreviewShell = host.Shell;
            _ = host.StartAsync();
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

    private void OnDefinitionsChanged(VariableScope scope)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) ReloadEditors();
        else Avalonia.Threading.Dispatcher.UIThread.Post(ReloadEditors);
    }

    private void ReloadEditors()
    {
        PlayerVariables?.SetCurrentValueVisibility(false);
        SaveVariables?.SetCurrentValueVisibility(false);
        if (_disposed || _projectService.Current is null)
        {
            PlayerVariables = null;
            SaveVariables = null;
            return;
        }

        PlayerVariables = new VariableListEditorViewModel(
            _variableDefinitions.GetDefinitions(VariableScope.Player),
            showCurrentValue: true,
            name => _variableService.GetSnapshot(VariableScope.Player).TryGetValue(name, out var v) ? CloneVariable(v, name) : null,
            (name, value) =>
            {
                _variableService.GetSnapshot(VariableScope.Player).TryGetValue(name, out var existing);

                var v = existing is not null ? CloneVariable(existing, name) : new Variable { Name = name };
                v.SetValue(value);
                _variableService.NotifyVariableChanged(VariableScope.Player, name, v);
            });

        SaveVariables = new VariableListEditorViewModel(
            _variableDefinitions.GetDefinitions(VariableScope.Save),
            showCurrentValue: IsGameStarted,
            name => _variableService.GetSnapshot(VariableScope.Save).TryGetValue(name, out var v) ? CloneVariable(v, name) : null,
            (name, value) =>
            {
                if (_runtime is null) return;
                _runtime.SetVariable(name, value);
            });

        OnPropertyChanged(nameof(PlayerVariables));
        OnPropertyChanged(nameof(SaveVariables));
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
        PreviewShell = null;
        var host = _previewHost;
        _previewHost = null;
        if (host is not null)
            await host.DisposeAsync();
        ReloadEditors();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposePreviewForProjectCloseAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Dock.Model disposes document contexts synchronously. Start the same idempotent
    /// cleanup path without blocking the UI thread that owns the document close.
    /// </summary>
    public void Dispose()
    {
        _ = DisposeAsync().AsTask();
        GC.SuppressFinalize(this);
    }

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
            _variableDefinitions.DefinitionsChanged -= OnDefinitionsChanged;
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
