using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Project;
using Serilog;

namespace GalNet.Editor.Services;

public sealed class EditorPlayerVariableStore : IEditorPlayerVariableStore
{
    private readonly IProjectService _projectService;
    private readonly IVariableDefinitionService _variableDefinitions;
    private readonly Dictionary<string, Variable> _variables = new(StringComparer.Ordinal);

    public event Action? Changed;

    public IReadOnlyDictionary<string, Variable> Snapshot => _variables;

    public EditorPlayerVariableStore(IProjectService projectService, IVariableDefinitionService variableDefinitions)
    {
        _projectService = projectService;
        _variableDefinitions = variableDefinitions;
        _projectService.CurrentChanged += _ => Reload();
        _variableDefinitions.DefinitionsChanged += OnDefinitionsChanged;
        Reload();
    }

    public void Reload()
    {
        _variables.Clear();

        if (_projectService.Current is not { } project)
        {
            Changed?.Invoke();
            return;
        }

        var path = GetStoragePath(project);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var restored = JsonSerializer.Deserialize<Dictionary<string, Variable>>(json) ?? [];
                foreach (var definition in _variableDefinitions.GetDefinitions(VariableScope.Player))
                {
                    if (restored.TryGetValue(definition.Name, out var variable))
                        _variables[definition.Name] = CloneVariable(definition.Name, variable);
                    else
                        _variables[definition.Name] = CloneVariable(definition.Name, definition.DefaultValue);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to reload editor player variables from {Path}", path);
            }
        }

        EnsureInitialized();
        Changed?.Invoke();
    }

    public IReadOnlyDictionary<string, Variable> EnsureInitialized()
    {
        var definitions = _variableDefinitions.GetDefinitions(VariableScope.Player);

        var staleNames = _variables.Keys.Except(definitions.Select(v => v.Name), StringComparer.Ordinal).ToList();
        foreach (var staleName in staleNames)
            _variables.Remove(staleName);

        foreach (var definition in definitions)
        {
            if (_variables.TryGetValue(definition.Name, out var variable) && variable.Type == definition.Type)
                continue;

            _variables[definition.Name] = CloneVariable(definition.Name, definition.DefaultValue);
        }

        Save();
        return Snapshot;
    }

    public void Reset()
    {
        _variables.Clear();
        foreach (var definition in _variableDefinitions.GetDefinitions(VariableScope.Player))
            _variables[definition.Name] = CloneVariable(definition.Name, definition.DefaultValue);

        Save();
        Changed?.Invoke();
    }

    public void SetValue(string name, object value)
    {
        if (!VariableNameRules.IsValid(name))
            return;

        if (!_variables.TryGetValue(name, out var variable))
            variable = _variables[name] = new Variable { Name = name };

        variable.SetValue(value);
        Save();
        Changed?.Invoke();
    }

    public void SetVariable(string name, Variable variable)
    {
        if (!VariableNameRules.IsValid(name))
            return;

        _variables[name] = CloneVariable(name, variable);
        Save();
        Changed?.Invoke();
    }

    public void Remove(string name)
    {
        if (_variables.Remove(name))
        {
            Save();
            Changed?.Invoke();
        }
    }

    private void Save()
    {
        if (_projectService.Current is not { } project)
            return;

        try
        {
            Directory.CreateDirectory(project.EditorStateDirectory);
            var json = JsonSerializer.Serialize(_variables, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetStoragePath(project), json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save editor player variables");
        }
    }

    private static string GetStoragePath(GalProject project) =>
        Path.Combine(project.EditorStateDirectory, "player-variables.json");

    private void OnDefinitionsChanged(VariableScope scope)
    {
        if (scope != VariableScope.Player)
            return;

        EnsureInitialized();
        Changed?.Invoke();
    }

    private static Variable CloneVariable(string name, Variable source)
    {
        var clone = new Variable { Name = name };
        switch (source.Type)
        {
            case VariableType.Bool:
                clone.SetValue(source.AsBool());
                break;
            case VariableType.Int:
                clone.SetValue(source.AsInt());
                break;
            case VariableType.Float:
                clone.SetValue(source.AsFloat());
                break;
            default:
                clone.SetValue(source.AsString());
                break;
        }

        return clone;
    }
}
