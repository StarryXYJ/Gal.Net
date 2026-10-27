using GalNet.Core.Serialization;
using System.Text.Json;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

/// <summary>Effect metadata uses the same field/value rules as Entry contracts.</summary>
internal static class ParameterValidation
{
    public static FieldDescriptor Field(string name, Type type, bool required, float? minimum, float? maximum, IReadOnlyList<string>? options = null)
    {
        var constraints = new Dictionary<string, object>();
        if (minimum is { } min) constraints["minimum"] = min;
        if (maximum is { } max) constraints["maximum"] = max;
        if (options is { Count: > 0 }) constraints["enum"] = options;
        return new(name, new ValueDescriptor(type, allowNull: false, constraints: JsonCodec.SerializeToElement(constraints)), required);
    }
    public static IReadOnlyList<string> Validate(string kind, string id, JsonElement values, IReadOnlyDictionary<string, FieldDescriptor> fields)
    {
        if (values.ValueKind != JsonValueKind.Object) return [$"{kind} '{id}' parameters must be a JSON object."];
        var diagnostics = new List<string>();
        foreach (var value in values.EnumerateObject())
        {
            if (!fields.TryGetValue(value.Name, out var field)) diagnostics.Add($"{kind} '{id}' does not declare parameter '{value.Name}'.");
            else if (ValueValidation.Validate(field.Value, value.Value) is { } issue)
                diagnostics.Add($"{kind} parameter '{value.Name}' {(issue.Message.StartsWith("Value ", StringComparison.Ordinal) ? issue.Message[6..] : issue.Message)}");
        }
        foreach (var field in fields.Values)
            if (field.IsRequired && !values.TryGetProperty(field.Id, out _)) diagnostics.Add($"{kind} '{id}' requires parameter '{field.Id}'.");
        return diagnostics;
    }
}
