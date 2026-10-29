using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

/// <summary>Read-only declaration snapshots and notifications for authoring queries.</summary>
public sealed class VariableDefinitionService(IEditorDocumentService documents) : IVariableDefinitionService
{
    public event Action<VariableScope>? DefinitionsChanged;

    public IReadOnlyList<ProjectVariableDefinition> GetDefinitions(VariableScope scope) => (scope switch
    {
        VariableScope.Player => documents.CurrentDocument.PlayerVariables,
        VariableScope.Save => documents.CurrentDocument.SaveVariables,
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    }).Select(definition => definition.Clone()).ToArray();

    public void ResetFromDocument()
    {
        DefinitionsChanged?.Invoke(VariableScope.Player);
        DefinitionsChanged?.Invoke(VariableScope.Save);
    }
}
