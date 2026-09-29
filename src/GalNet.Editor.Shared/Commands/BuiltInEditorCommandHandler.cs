using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Commands;

public sealed partial class BuiltInEditorCommandHandler : IEditorCommandHandler
{
    public bool CanHandle(IProjectEditCommand command) => Domains.Any(domain => domain.CanHandle(command));

    public CommandExecution Execute(
        EditorProjectDocument document,
        IProjectEditCommand command,
        EditorCommandContext context) => Domains.FirstOrDefault(domain => domain.CanHandle(command))?.Execute(document, command, context)
            ?? CommandExecution.Failed(EditorDiagnostic.Error("command.unsupported", $"Unsupported command '{command.CommandId}'."));

    private static CommandExecution InvalidIndex(string kind, int index, int count) =>
        Error("command.invalidIndex", $"Index {index} is outside the valid range for {kind} collection of size {count}.");

    private static CommandExecution NotFound(string code, string kind, string id, string resource) =>
        Error(code, $"{kind} '{id}' does not exist.", resource);

    private static CommandExecution Error(string code, string message, string? resource = null) =>
        CommandExecution.Failed(EditorDiagnostic.Error(code, message, resource));

    private static CommandExecution Success(
        string description,
        string displayNameKey,
        string changedResource,
        params object?[] arguments) =>
        CommandExecution.Succeeded(description, displayNameKey, [changedResource], arguments);

    private static CommandExecution Success(
        string description,
        string displayNameKey,
        IReadOnlyList<string> changedResources,
        params object?[] arguments) =>
        CommandExecution.Succeeded(description, displayNameKey, changedResources, arguments);

    private static string NodeResource(string nodeId) => $"graph/nodes/{nodeId}";
    private static string EdgeResource(string edgeId) => $"graph/edges/{edgeId}";
    private static string EntryResource(string groupId, string entryId) => $"groups/{groupId}/entries/{entryId}";
    private static string OptionResource(string nodeId, string optionId) => $"graph/nodes/{nodeId}/options/{optionId}";
    private static string ConditionResource(string nodeId, string conditionId) => $"graph/nodes/{nodeId}/conditions/{conditionId}";
    private static string VariableResource(VariableScope scope, string name) => $"variables/{scope.ToString().ToLowerInvariant()}/{name}";
}
