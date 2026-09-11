using System.Text.Json;
using GalNet.Core.I18n;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Commands;

/// <summary>Metadata used to expose an editor command to UI and automation clients.</summary>
public interface IEditorCommandDefinition
{
    string Id { get; }
    string Description { get; }
    I18nKey DisplayNameKey { get; }
}

/// <summary>Metadata for a command that mutates an editable project document.</summary>
public interface IProjectCommandDefinition : IEditorCommandDefinition
{
    Type CommandType { get; }
    EditorCommandSchema Schema { get; }
}

/// <summary>Serializable parameter contract for a project command.</summary>
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

/// <summary>Resolves command schemas and deserializes their JSON payloads into typed edit commands.</summary>
public interface IEditorCommandCatalog
{
    IReadOnlyList<IProjectCommandDefinition> GetAll();
    IProjectCommandDefinition? Find(string commandId);
    IProjectEditCommand Deserialize(string commandId, JsonElement payload, JsonSerializerOptions? options = null);
}

/// <summary>Applies a typed edit command to a document at a specific revision.</summary>
public interface IEditorCommandHandler
{
    bool CanHandle(IProjectEditCommand command);
    CommandExecution Execute(EditorProjectDocument document, IProjectEditCommand command, EditorCommandContext context);
}

/// <summary>Execution settings supplied by the command coordinator.</summary>
/// <param name="Revision">Revision the command is evaluated against.</param>
/// <param name="IsDryRun">When true, handlers validate and describe changes without committing them.</param>
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

/// <summary>Concurrency, preview, and history-merge options for one command execution.</summary>
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

/// <summary>Validation outcome; only error diagnostics make the result invalid.</summary>
public sealed record ValidationResult(IReadOnlyList<EditorDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != EditorDiagnosticSeverity.Error);
}
