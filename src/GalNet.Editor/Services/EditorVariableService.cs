using System;
using System.Collections.Generic;
using System.Linq;
using GalNet.Core.Services;
using GalNet.Core.Variable;
using GalNet.Editor.Project;

namespace GalNet.Editor.Services;

/// <summary>
/// Editor implementation of IVariableService.
/// Wraps EditorPlayerVariableStore for player variables and manages save variables in-memory.
/// </summary>
public sealed class EditorVariableService : IVariableService
{
    private readonly IEditorPlayerVariableStore _playerStore;
    private readonly IProjectService _projectService;
    private readonly Dictionary<string, Variable> _saveVariables = new(StringComparer.Ordinal);

    public event Action<VariableScope, string, Variable>? VariableChanged;

    public EditorVariableService(IEditorPlayerVariableStore playerStore, IProjectService projectService)
    {
        _playerStore = playerStore;
        _projectService = projectService;
    }

    public IReadOnlyDictionary<string, Variable> GetSnapshot(VariableScope scope)
    {
        if (scope == VariableScope.Player)
            return _playerStore.Snapshot;

        if (_saveVariables.Count == 0)
        {
            // Initialize save variables from project settings defaults
            if (_projectService.Current?.Settings is { } settings)
            {
                foreach (var def in settings.SaveVariables)
                {
                    if (!_saveVariables.ContainsKey(def.Name))
                        _saveVariables[def.Name] = CloneVariable(def.DefaultValue, def.Name);
                }
            }
        }

        return _saveVariables;
    }

    public VariableScope ResolveScope(string name)
    {
        if (_projectService.Current?.Settings is { } settings)
            return settings.SaveVariables.Any(v => string.Equals(v.Name, name, StringComparison.Ordinal))
                ? VariableScope.Save
                : VariableScope.Player;

        return VariableScope.Player;
    }

    /// <summary>Notify that a variable changed in the runtime. Syncs back to editor state.</summary>
    public void NotifyVariableChanged(VariableScope scope, string name, Variable variable)
    {
        if (scope == VariableScope.Player)
            _playerStore.SetVariable(name, variable);
        else
            _saveVariables[name] = CloneVariable(variable, name);

        VariableChanged?.Invoke(scope, name, variable);
    }

    public void ResetAll()
    {
        if (_projectService.Current?.Settings is not { } settings)
            return;

        _saveVariables.Clear();
        _playerStore.Reset(settings);
    }

    private static Variable CloneVariable(Variable source, string name)
    {
        var clone = new Variable { Name = name };
        clone.SetValue(source.Type switch
        {
            VariableType.Bool => source.AsBool(),
            VariableType.Int => source.AsInt(),
            VariableType.Float => source.AsFloat(),
            _ => source.AsString()
        });
        return clone;
    }
}