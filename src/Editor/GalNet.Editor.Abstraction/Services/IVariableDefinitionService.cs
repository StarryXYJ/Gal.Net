using System;
using System.Collections.Generic;
using GalNet.Core.Variable;

namespace GalNet.Editor.Abstraction.Services;

public interface IVariableDefinitionService
{
    event Action<VariableScope>? DefinitionsChanged;

    /// <summary>Detached snapshots; author changes go through project edit intents.</summary>
    IReadOnlyList<ProjectVariableDefinition> GetDefinitions(VariableScope scope);

    void ResetFromDocument();
}
