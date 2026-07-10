using System;
using System.Collections.Generic;
using System.Linq;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

public sealed class VariableDefinitionService : IVariableDefinitionService
{
    private readonly IEditorDocumentService _documentService;

    public event Action<VariableScope>? DefinitionsChanged;

    public VariableDefinitionService(IEditorDocumentService documentService)
    {
        _documentService = documentService;
    }

    public List<ProjectVariableDefinition> GetDefinitions(VariableScope scope) =>
        GetList(scope);

    public bool IsNameAvailable(string name, VariableScope scope, ProjectVariableDefinition? current = null)
    {
        var sanitized = VariableNameRules.Sanitize(name, current?.Name ?? $"var_{scope.ToString().ToLowerInvariant()}");
        return GetAllDefinitions()
            .Where(def => !ReferenceEquals(def, current))
            .All(def => !string.Equals(def.Name, sanitized, StringComparison.Ordinal));
    }

    public ProjectVariableDefinition AddDefinition(VariableScope scope)
    {
        var list = GetMutableList(scope);
        var index = list.Count + 1;
        var baseName = $"var_{scope.ToString().ToLowerInvariant()}_{index}";
        var name = VariableNameRules.Sanitize(baseName);
        var suffix = 1;
        while (!IsNameAvailable(name, scope))
            name = $"{baseName}_{suffix++}";

        var definition = new ProjectVariableDefinition
        {
            Name = name,
            DefaultValue = new Variable { Name = name }
        };
        list.Add(definition);
        MarkChanged(scope);
        return definition;
    }

    public bool RemoveDefinition(VariableScope scope, ProjectVariableDefinition definition)
    {
        var removed = GetMutableList(scope).Remove(definition);
        if (removed)
            MarkChanged(scope);
        return removed;
    }

    public bool RenameDefinition(VariableScope scope, ProjectVariableDefinition definition, string newName)
    {
        var sanitized = VariableNameRules.Sanitize(newName, definition.Name);
        if (string.Equals(definition.Name, sanitized, StringComparison.Ordinal))
            return true;

        if (!IsNameAvailable(sanitized, scope, definition))
            return false;

        definition.Name = sanitized;
        definition.DefaultValue.Name = sanitized;
        MarkChanged(scope);
        return true;
    }

    public bool UpdateDefinition(ProjectVariableDefinition definition)
    {
        if (!GetAllDefinitions().Any(def => ReferenceEquals(def, definition)))
            return false;

        definition.DefaultValue.Name = definition.Name;
        var scope = GetScope(definition);
        MarkChanged(scope);
        return true;
    }

    public void ResetFromDocument()
    {
        DefinitionsChanged?.Invoke(VariableScope.Player);
        DefinitionsChanged?.Invoke(VariableScope.Save);
    }

    private VariableScope GetScope(ProjectVariableDefinition definition) =>
        GetMutableList(VariableScope.Save).Any(def => ReferenceEquals(def, definition))
            ? VariableScope.Save
            : VariableScope.Player;

    private IReadOnlyList<ProjectVariableDefinition> GetAllDefinitions() =>
        [.. _documentService.CurrentDocument.PlayerVariables, .. _documentService.CurrentDocument.SaveVariables];

    private List<ProjectVariableDefinition> GetMutableList(VariableScope scope) =>
        scope == VariableScope.Player
            ? _documentService.CurrentDocument.PlayerVariables
            : _documentService.CurrentDocument.SaveVariables;

    private List<ProjectVariableDefinition> GetList(VariableScope scope) => GetMutableList(scope);

    private void MarkChanged(VariableScope scope)
    {
        VariableNameRules.Normalize(
            _documentService.CurrentDocument.PlayerVariables,
            _documentService.CurrentDocument.SaveVariables);
        _documentService.MarkDirty();
        DefinitionsChanged?.Invoke(scope);
    }
}
