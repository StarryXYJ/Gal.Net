using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.ViewModels;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Variable;
using GalNet.Editor.Project;
using GalNet.Editor.Services;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class GamePreviewPanelViewModel : ObservableObject
{
    private readonly GalNet.Core.Services.INavigationService _navigation;
    private readonly IGameFlowFactory _gameFlowFactory;
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly IProjectService _projectService;
    private readonly EditorVariableService _variableService;

    private IGameRuntime? _runtime;

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

    public ObservableCollection<string> OutputLines { get; } = [];

    public GamePreviewPanelViewModel(
        GalNet.Core.Services.INavigationService navigation,
        IGameFlowFactory gameFlowFactory,
        EditorWorkspaceViewModel workspace,
        IProjectService projectService,
        EditorVariableService variableService)
    {
        _navigation = navigation;
        _gameFlowFactory = gameFlowFactory;
        _workspace = workspace;
        _projectService = projectService;
        _variableService = variableService;

        _projectService.CurrentChanged += _ => ReloadEditors();
        _variableService.VariableChanged += OnVariableServiceChanged;

        ReloadEditors();
        _ = RestartPreviewAsync();
    }

    public void FocusInspector() => _workspace.FocusPreview(this);

    [RelayCommand]
    private async Task RestartPreviewAsync()
    {
        if (_projectService.Current is not { } project)
        {
            StatusText = "No project";
            return;
        }

        StatusText = "Restarting...";
        _runtime = null;
        IsGameStarted = false;

        var options = new GameFlowOptions
        {
            Title = project.Name,
            UseSampleDataIfMissing = false,
            VariableService = _variableService,
            RuntimeCreated = OnRuntimeCreated
        };

        PageHostVm = _gameFlowFactory.CreatePageHost(_navigation, options);
        ReloadEditors();
        OutputLines.Insert(0, $"Preview restarted at {DateTime.Now:T}");
        StatusText = "Ready";
    }

    public async Task ResetPlayerAsync()
    {
        _variableService.ResetAll();
        await RestartPreviewAsync();
    }

    private void OnVariableServiceChanged(VariableScope scope, string name, Variable variable)
    {
        // Update runtime as well when editor modifies variables
        if (scope == VariableScope.Player)
            _runtime?.SetVariable(name, GetRawValue(variable));
    }

    private void ReloadEditors()
    {
        if (_projectService.Current?.Settings is not { } settings)
            return;

        VariableNameRules.Normalize(settings);

        PlayerVariables = new VariableListEditorViewModel(
            settings.PlayerVariables,
            VariableScope.Player,
            showCurrentValue: true,
            allowCurrentEditing: true,
            IsNameAvailable,
            name => _variableService.GetSnapshot(VariableScope.Player).TryGetValue(name, out var v) ? CloneVariable(v, name) : null,
            (name, value) =>
            {
                _variableService.GetSnapshot(VariableScope.Player).TryGetValue(name, out var existing);

                var v = existing is not null ? CloneVariable(existing, name) : new Variable { Name = name };
                v.SetValue(value);
                _variableService.NotifyVariableChanged(VariableScope.Player, name, v);
                _runtime?.SetVariable(name, value);
            },
            name => { /* Remove handled by rename */ },
            RenamePlayerVariable,
            PersistVariableDefinitions,
            OnNameConflict);

        SaveVariables = new VariableListEditorViewModel(
            settings.SaveVariables,
            VariableScope.Save,
            showCurrentValue: IsGameStarted,
            allowCurrentEditing: IsGameStarted,
            IsNameAvailable,
            name => _variableService.GetSnapshot(VariableScope.Save).TryGetValue(name, out var v) ? CloneVariable(v, name) : null,
            (name, value) =>
            {
                if (_runtime is null) return;
                _runtime.SetVariable(name, value);
            },
            name => { /* Remove handled by rename */ },
            RenameSaveVariable,
            PersistVariableDefinitions);

        OnPropertyChanged(nameof(PlayerVariables));
        OnPropertyChanged(nameof(SaveVariables));
    }

    private bool IsNameAvailable(string name, VariableScope scope)
    {
        if (_projectService.Current?.Settings is not { } settings)
            return true;

        var allNames = settings.PlayerVariables.Select(v => v.Name)
            .Concat(settings.SaveVariables.Select(v => v.Name))
            .ToList();
        var count = allNames.Count(n => string.Equals(n, name, StringComparison.Ordinal));
        return count <= 1;
    }

    private void RenamePlayerVariable(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        if (_variableService.GetSnapshot(VariableScope.Player).TryGetValue(oldName, out var variable))
        {
            var cloned = CloneVariable(variable, newName);
            _variableService.NotifyVariableChanged(VariableScope.Player, newName, cloned);
            _runtime?.SetVariable(newName, GetRawValue(cloned));
        }
    }

    private void RenameSaveVariable(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        if (_variableService.GetSnapshot(VariableScope.Save).TryGetValue(oldName, out var variable))
        {
            var cloned = CloneVariable(variable, newName);
            _variableService.NotifyVariableChanged(VariableScope.Save, newName, cloned);
        }
    }

    private void PersistVariableDefinitions()
    {
        if (_projectService.Current is not { } project)
            return;

        VariableNameRules.Normalize(project.Settings);
        project.IsDirty = true;
        _ = _projectService.SaveAsync();
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
        IsGameStarted = true;
        ReloadEditors();
        StatusText = "Running";
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