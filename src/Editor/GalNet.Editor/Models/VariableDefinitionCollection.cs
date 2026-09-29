using System.Collections.Generic;
using GalNet.Core.Variable;

namespace GalNet.Editor.Models;

/// <summary>Owns ordered editor variable definitions and their collection invariants.</summary>
public sealed class VariableDefinitionCollection
{
    private readonly List<ProjectVariableDefinition> _definitions;
    public VariableDefinitionCollection(List<ProjectVariableDefinition> definitions) => _definitions = definitions;
    public int Count => _definitions.Count;
    public IEnumerable<ProjectVariableDefinition> Items => _definitions;
    public ProjectVariableDefinition Add(string name)
    {
        var definition = new ProjectVariableDefinition { Name = name, DefaultValue = new Variable { Name = name } };
        _definitions.Add(definition);
        return definition;
    }
    public bool Remove(ProjectVariableDefinition definition) => _definitions.Remove(definition);
    public bool Move(ProjectVariableDefinition definition, int newIndex)
    {
        var oldIndex = _definitions.IndexOf(definition);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= _definitions.Count || oldIndex == newIndex) return false;
        _definitions.RemoveAt(oldIndex);
        _definitions.Insert(newIndex, definition);
        return true;
    }
}
