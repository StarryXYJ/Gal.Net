using System;
using System.Collections.Generic;
using System.Linq;
using GalNet.Core.Services;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Project;

namespace GalNet.Editor.Services;

/// <summary>
/// Editor implementation of IVariableService.
/// Wraps EditorPlayerVariableStore for player variables and manages save variables in-memory.
/// </summary>
public sealed class EditorVariableService : IVariableService
{
    private readonly IEditorPlayerVariableStore _playerStore;
    private readonly IVariableDefinitionService _variableDefinitions;
    private readonly Dictionary<string, Variable> _saveVariables = new(StringComparer.Ordinal);

    public event Action<VariableScope, string, Variable>? VariableChanged;

    public EditorVariableService(
        IEditorPlayerVariableStore playerStore,
        IVariableDefinitionService variableDefinitions)
    {
        _playerStore = playerStore;
        _variableDefinitions = variableDefinitions;
        _variableDefinitions.DefinitionsChanged += OnDefinitionsChanged;
    }

    public IReadOnlyDictionary<string, Variable> GetSnapshot(VariableScope scope)
    {
        if (scope == VariableScope.Player)
            return _playerStore.Snapshot;

        if (_saveVariables.Count == 0)
        {
            foreach (var def in _variableDefinitions.GetDefinitions(VariableScope.Save))
            {
                if (!_saveVariables.ContainsKey(def.Name))
                    _saveVariables[def.Name] = CloneVariable(def.DefaultValue, def.Name);
            }
        }

        return _saveVariables;
    }

    public VariableScope ResolveScope(string name)
    {
        return _variableDefinitions.GetDefinitions(VariableScope.Save)
            .Any(v => string.Equals(v.Name, name, StringComparison.Ordinal))
            ? VariableScope.Save
            : VariableScope.Player;
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
        _saveVariables.Clear();
        foreach (var definition in _variableDefinitions.GetDefinitions(VariableScope.Save))
            _saveVariables[definition.Name] = CloneVariable(definition.DefaultValue, definition.Name);
        _playerStore.Reset();
    }

    public void RemoveRuntimeVariable(VariableScope scope, string name)
    {
        if (scope == VariableScope.Player)
        {
            _playerStore.Remove(name);
            return;
        }

        _saveVariables.Remove(name);
    }

    public void RenameRuntimeVariable(VariableScope scope, string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        if (scope == VariableScope.Player)
        {
            if (_playerStore.Snapshot.TryGetValue(oldName, out var current))
            {
                _playerStore.SetVariable(newName, CloneVariable(current, newName));
                _playerStore.Remove(oldName);
            }

            return;
        }

        if (_saveVariables.TryGetValue(oldName, out var saveValue))
        {
            _saveVariables[newName] = CloneVariable(saveValue, newName);
            _saveVariables.Remove(oldName);
        }
    }

    private void OnDefinitionsChanged(VariableScope scope)
    {
        if (scope != VariableScope.Save)
            return;

        var definitions = _variableDefinitions.GetDefinitions(VariableScope.Save);
        var staleNames = _saveVariables.Keys.Except(definitions.Select(v => v.Name), StringComparer.Ordinal).ToList();
        foreach (var stale in staleNames)
            _saveVariables.Remove(stale);

        foreach (var definition in definitions)
        {
            if (_saveVariables.TryGetValue(definition.Name, out var variable) && variable.Type == definition.Type)
                continue;

            _saveVariables[definition.Name] = CloneVariable(definition.DefaultValue, definition.Name);
        }
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
