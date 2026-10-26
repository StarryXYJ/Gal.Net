using System;
using System.Collections.Generic;
using System.Linq;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.ViewModels;

public partial class EditorWorkspaceViewModel
{
    private readonly IEditorDocumentService _documentService;
    public IReadOnlyList<ProjectVariableDefinition> AllProjectVariableDefinitions =>
        [.. _variableDefinitionService.GetDefinitions(VariableScope.Player), .. _variableDefinitionService.GetDefinitions(VariableScope.Save)];

    public IReadOnlyList<ConditionVariableSuggestion> GetConditionVariableSuggestions()
    {
        return _variableDefinitionService.GetDefinitions(VariableScope.Player)
                .Select(v => new ConditionVariableSuggestion { Name = v.Name, Scope = VariableScope.Player })
                .Concat(_variableDefinitionService.GetDefinitions(VariableScope.Save).Select(v => new ConditionVariableSuggestion { Name = v.Name, Scope = VariableScope.Save }))
                .ToList();
    }

}
