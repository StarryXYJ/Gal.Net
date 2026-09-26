using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalNet.Core.Gallery;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using Serilog;

namespace GalNet.Editor.Shared.Services;

public sealed class EditorPlayerVariableStore : IEditorPlayerVariableStore
{
    private readonly IProjectService _projectService;
    private readonly IVariableDefinitionService _variableDefinitions;
    private readonly Dictionary<string, Variable> _variables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SystemVariableDefinition> _systemVariables = new(StringComparer.Ordinal);

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

    public void ConfigureSystemVariables(IReadOnlyCollection<SystemVariableDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var configured = definitions
            .Where(definition => definition.Scope == VariableScope.Player)
            .ToDictionary(definition => definition.Name, StringComparer.Ordinal);

        _systemVariables.Clear();
        foreach (var pair in configured)
            _systemVariables.Add(pair.Key, pair.Value);

        EnsureInitialized();
        Changed?.Invoke();
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
                foreach (var pair in restored.Where(pair => GalleryUnlockVariable.IsReservedName(pair.Key)))
                    _variables[pair.Key] = CloneVariable(pair.Key, pair.Value);
                foreach (var definition in GetDefinitions())
                {
                    if (restored.TryGetValue(definition.Name, out var variable)
                        && variable.Type == definition.DefaultValue.Type)
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
        var definitions = GetDefinitions();

        var staleNames = _variables.Keys
            .Except(definitions.Select(v => v.Name), StringComparer.Ordinal)
            .Where(name => !GalleryUnlockVariable.IsReservedName(name))
            .ToList();
        foreach (var staleName in staleNames)
            _variables.Remove(staleName);

        foreach (var definition in definitions)
        {
            if (_variables.TryGetValue(definition.Name, out var variable)
                && variable.Type == definition.DefaultValue.Type)
                continue;

            _variables[definition.Name] = CloneVariable(definition.Name, definition.DefaultValue);
        }

        Save();
        return Snapshot;
    }

    public void Reset()
    {
        _variables.Clear();
        foreach (var definition in GetDefinitions())
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
            Directory.CreateDirectory(Path.GetDirectoryName(GetStoragePath(project))!);
            var json = JsonSerializer.Serialize(_variables, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetStoragePath(project), json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save editor player variables");
        }
    }

    private static string GetStoragePath(GalProject project) =>
        Path.Combine(project.EditorStateDirectory, "player", "player-variables.json");

    private void OnDefinitionsChanged(VariableScope scope)
    {
        if (scope != VariableScope.Player)
            return;

        EnsureInitialized();
        Changed?.Invoke();
    }

    private IReadOnlyList<(string Name, Variable DefaultValue)> GetDefinitions()
    {
        var definitions = new Dictionary<string, Variable>(StringComparer.Ordinal);
        foreach (var definition in _systemVariables.Values)
            definitions[definition.Name] = definition.DefaultValue;
        foreach (var definition in _variableDefinitions.GetDefinitions(VariableScope.Player))
            definitions.TryAdd(definition.Name, definition.DefaultValue);
        return definitions.Select(pair => (pair.Key, pair.Value)).ToList();
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
