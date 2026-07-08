using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Editor.Project;
using Serilog;

namespace GalNet.Editor.Services;

public sealed class EditorPlayerVariableStore : IEditorPlayerVariableStore
{
    private readonly IProjectService _projectService;
    private readonly Dictionary<string, Variable> _variables = new(StringComparer.Ordinal);

    public event Action? Changed;

    public IReadOnlyDictionary<string, Variable> Snapshot => _variables;

    public EditorPlayerVariableStore(IProjectService projectService)
    {
        _projectService = projectService;
        _projectService.CurrentChanged += _ => Reload();
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

        VariableNameRules.Normalize(project.Settings);

        var path = GetStoragePath(project);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var restored = JsonSerializer.Deserialize<Dictionary<string, Variable>>(json) ?? [];
                foreach (var definition in project.Settings.PlayerVariables)
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

        EnsureInitialized(project.Settings);
        Changed?.Invoke();
    }

    public IReadOnlyDictionary<string, Variable> EnsureInitialized(ProjectSettings settings)
    {
        VariableNameRules.Normalize(settings);

        var staleNames = _variables.Keys.Except(settings.PlayerVariables.Select(v => v.Name), StringComparer.Ordinal).ToList();
        foreach (var staleName in staleNames)
            _variables.Remove(staleName);

        foreach (var definition in settings.PlayerVariables)
        {
            if (_variables.TryGetValue(definition.Name, out var variable) && variable.Type == definition.Type)
                continue;

            _variables[definition.Name] = CloneVariable(definition.Name, definition.DefaultValue);
        }

        Save();
        return Snapshot;
    }

    public void Reset(ProjectSettings settings)
    {
        _variables.Clear();
        foreach (var definition in settings.PlayerVariables)
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
