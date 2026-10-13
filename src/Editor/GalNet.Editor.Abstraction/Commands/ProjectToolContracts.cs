using System.Text.Json;

namespace GalNet.Editor.Abstraction.Commands;

public sealed record ToolDiagnostic(string Code, string Severity, string Message,
    string? File = null, string? ObjectId = null, string? JsonPointer = null, long? Line = null, long? Column = null);

/// <summary>Stable machine result shared by CLI and future editor adapters.</summary>
public sealed record ToolResult(string OperationId, bool Success, object? Data, IReadOnlyList<ToolDiagnostic> Diagnostics)
{
    public int ProtocolVersion { get; init; } = 1;
}

public sealed record CliParameter(string Name, string Type, string Description,
    bool Required = false, bool Positional = false, object? Default = null, IReadOnlyList<string>? Enum = null,
    JsonElement? Schema = null);

/// <summary>One operation description, independent of CLI rendering or UI binding.</summary>
public sealed record ToolOperationDefinition(
    string OperationId, string CommandPath, string Description, string Usage,
    IReadOnlyList<CliParameter> Parameters, JsonElement InputSchema, JsonElement OutputSchema,
    bool ReadOnly, bool RequiresProject, bool RequiresContract,
    bool SupportsDryRun, string BatchMode,
    IReadOnlyList<string> Examples, IReadOnlyDictionary<int, string> ExitCodes,
    string Transport = "cli");
