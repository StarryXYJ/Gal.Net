using System.Text.Json;
using GalNet.Core.I18n;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Commands;

public interface IEditorCommandDefinition
{
    string Id { get; }
    string Description { get; }
    I18nKey DisplayNameKey { get; }
}

public interface IProjectCommandDefinition : IEditorCommandDefinition
{
    Type CommandType { get; }
    EditorCommandSchema Schema { get; }
}

public sealed record EditorCommandSchema(IReadOnlyList<EditorCommandParameter> Parameters);

public sealed record EditorCommandParameter(
    string Name,
    string Type,
    bool Required,
    string Description);

public interface IEditorCommand
{
    string CommandId { get; }
}

public interface IProjectEditCommand : IEditorCommand;

public interface IEditorCommandCatalog
{
    IReadOnlyList<IProjectCommandDefinition> GetAll();
    IProjectCommandDefinition? Find(string commandId);
    IProjectEditCommand Deserialize(string commandId, JsonElement payload, JsonSerializerOptions? options = null);
}

public interface IEditorCommandHandler
{
    bool CanHandle(IProjectEditCommand command);
    CommandExecution Execute(EditorProjectDocument document, IProjectEditCommand command, EditorCommandContext context);
}

public sealed record EditorCommandContext(long Revision, bool IsDryRun);

public sealed record EditorExecutionDescription(
    string Description,
    I18nKey DisplayNameKey,
    IReadOnlyList<object?> DisplayNameArguments);

public sealed record EditorDiagnostic(
    string Code,
    EditorDiagnosticSeverity Severity,
    string Message,
    string? Resource = null)
{
    public static EditorDiagnostic Error(string code, string message, string? resource = null) =>
        new(code, EditorDiagnosticSeverity.Error, message, resource);

    public static EditorDiagnostic Warning(string code, string message, string? resource = null) =>
        new(code, EditorDiagnosticSeverity.Warning, message, resource);
}

public enum EditorDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record CommandExecution(
    bool Success,
    EditorExecutionDescription? Description,
    IReadOnlyList<string> ChangedResources,
    IReadOnlyList<EditorDiagnostic> Diagnostics)
{
    public static CommandExecution Succeeded(
        string description,
        string displayNameKey,
        IReadOnlyList<string> changedResources,
        params object?[] arguments) =>
        new(true, new EditorExecutionDescription(description, new I18nKey(displayNameKey), arguments), changedResources, []);

    public static CommandExecution Failed(params EditorDiagnostic[] diagnostics) =>
        new(false, null, [], diagnostics);
}

public sealed record ExecuteOptions(
    long? ExpectedRevision = null,
    bool DryRun = false,
    string? MergeKey = null,
    TimeSpan? MergeWindow = null);

public sealed record CommandResult(
    bool Success,
    long Revision,
    string? TransactionId,
    string? Description,
    I18nKey? DisplayNameKey,
    IReadOnlyList<object?> DisplayNameArguments,
    IReadOnlyList<string> ChangedResources,
    IReadOnlyList<EditorDiagnostic> Diagnostics)
{
    public static CommandResult Failure(long revision, params EditorDiagnostic[] diagnostics) =>
        new(false, revision, null, null, null, [], [], diagnostics);
}

public sealed record ValidationResult(IReadOnlyList<EditorDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != EditorDiagnosticSeverity.Error);
}
