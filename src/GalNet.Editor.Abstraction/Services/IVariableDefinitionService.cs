using System;
using System.Collections.Generic;
using GalNet.Core.Variable;

namespace GalNet.Editor.Abstraction.Services;

public interface IVariableDefinitionService
{
    event Action<VariableScope>? DefinitionsChanged;

    List<ProjectVariableDefinition> GetDefinitions(VariableScope scope);

    bool IsNameAvailable(string name, VariableScope scope, ProjectVariableDefinition? current = null);

    ProjectVariableDefinition AddDefinition(VariableScope scope);

    bool RemoveDefinition(VariableScope scope, ProjectVariableDefinition definition);

    bool RenameDefinition(VariableScope scope, ProjectVariableDefinition definition, string newName);

    bool UpdateDefinition(ProjectVariableDefinition definition);

    void ResetFromDocument();
}
