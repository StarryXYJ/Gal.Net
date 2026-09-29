using System.Text.Json;
using GalNet.Core.Primitives;

namespace GalNet.Core.Entry;

/// <summary>Normalizes primitive arguments from the entry definition's sole parameter schema.</summary>
public static class PrimitiveArgumentHelper
{
    public static JsonElement Normalize(DynamicParameterTable parameters, JsonElement arguments)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Primitive arguments must be a JSON object.");

        var normalized = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
        {
            if (!parameters.TryGetValue(property.Name, out var parameter))
                throw new InvalidDataException($"Unknown primitive parameter '{property.Name}'.");
            Validate(parameter, property.Value);
            normalized.Add(property.Name, property.Value.Clone());
        }

        foreach (var parameter in parameters.Values)
        {
            if (normalized.ContainsKey(parameter.Name))
                continue;
            if (parameter.DefaultValue is { } defaultValue)
                normalized.Add(parameter.Name, defaultValue.Clone());
            else if (parameter.IsRequired)
                throw new InvalidDataException($"Primitive requires parameter '{parameter.Name}'.");
        }

        return JsonSerializer.SerializeToElement(normalized);
    }

    public static (JsonElement Arguments, string? BatchId) NormalizeCompiledArguments(
        DynamicParameterTable parameters,
        JsonElement arguments)
    {
        var normalized = Normalize(parameters, arguments);
        string? batchId = null;
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in normalized.EnumerateObject())
        {
            if (property.NameEquals("batchId"))
            {
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new InvalidDataException("Primitive parameter 'batchId' must be a string.");
                batchId = property.Value.ValueKind == JsonValueKind.String
                    ? PrimitiveEntry.NormalizeBatchId(property.Value.GetString())
                    : null;
                continue;
            }
            values.Add(property.Name, property.Value.Clone());
        }
        return (JsonSerializer.SerializeToElement(values), batchId);
    }

    private static void Validate(DynamicParameterDescriptor parameter, JsonElement value)
    {
        try
        {
            DynamicParameterValue.Validate(value, parameter.ValueType, parameter.Name);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }

        if (!parameter.TryGetConstraint("options", out var options) || options.ValueKind != JsonValueKind.Array)
            return;
        var serialized = DynamicParameterValue.ToEditorValue(value);
        if (!options.EnumerateArray().Any(option => string.Equals(option.GetString(), serialized, StringComparison.Ordinal)))
            throw new InvalidDataException($"Primitive parameter '{parameter.Name}' has unsupported value '{serialized}'.");
    }
}
