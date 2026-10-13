using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Parameters;

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
    JsonElement? Schema = null,
    [property: JsonIgnore] ValueDescriptor? ValueContract = null)
{
    public static CliParameter FromValue(string name, ValueDescriptor value, string description, object? defaultValue = null)
    {
        var schema = ValueCodec.Describe(value);
        var nonNull = value.AllowNull ? schema.GetProperty("anyOf")[0] : schema;
        var exported = nonNull.GetProperty("allOf")[0];
        var type = exported.ValueKind == JsonValueKind.Object && exported.TryGetProperty("type", out var kind)
            ? kind.ValueKind == JsonValueKind.String ? kind.GetString()! : kind.EnumerateArray().First(item => item.GetString() != "null").GetString()!
            : exported.ValueKind == JsonValueKind.Object && exported.TryGetProperty("enum", out var choices) && choices.GetArrayLength() > 0 && choices[0].ValueKind == JsonValueKind.String ? "string" : "object";
        if (defaultValue is not null) _ = ValueCodec.Serialize(value, defaultValue);
        return new(name, type, description, Default: defaultValue, Schema: schema, ValueContract: value);
    }
}

/// <summary>One operation description, independent of CLI rendering or UI binding.</summary>
public sealed record ToolOperationDefinition(
    string OperationId, string CommandPath, string Description, string Usage,
    IReadOnlyList<CliParameter> Parameters, JsonElement InputSchema, JsonElement OutputSchema,
    bool ReadOnly, bool RequiresProject, bool RequiresContract,
    bool SupportsDryRun, string BatchMode,
    IReadOnlyList<string> Examples, IReadOnlyDictionary<int, string> ExitCodes,
    string Transport = "cli", JsonElement? DocumentInputSchema = null);
