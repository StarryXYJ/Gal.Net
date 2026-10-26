using System.Text.Json;
using GalNet.Core.Primitives;
using GalNet.Core.Parameters;

namespace GalNet.Core.Entry;

/// <summary>Normalizes primitive arguments from the entry definition's sole parameter schema.</summary>
public static class PrimitiveArgumentHelper
{
    public static JsonElement Normalize(DynamicParameterTable parameters, JsonElement arguments)
        => NormalizeCore(parameters, arguments, null);

    /// <summary>Validates once and writes decoded fields into a fresh invocation DTO, including independent defaults.</summary>
    public static (JsonElement Arguments, T Options) NormalizeFields<T>(DynamicParameterTable parameters, JsonElement arguments)
        where T : IFieldDto, new()
    {
        var options = new T();
        return (NormalizeCore(parameters, arguments, options), options);
    }

    private static JsonElement NormalizeCore(DynamicParameterTable parameters, JsonElement arguments, IFieldDto? options)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Primitive arguments must be a JSON object.");

        var normalized = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
        {
            if (!parameters.TryGetValue(property.Name, out var parameter))
                throw new InvalidDataException($"Unknown primitive parameter '{property.Name}'.");
            var decoded = Read(parameter, property.Value);
            options?.WriteField(parameter.Name, decoded);
            normalized.Add(property.Name, property.Value.Clone());
        }

        foreach (var parameter in parameters.Values)
        {
            if (normalized.ContainsKey(parameter.Name))
                continue;
            try
            {
                if (parameter.DefaultValue is { } defaultValue)
                {
                    normalized.Add(parameter.Name, defaultValue);
                    if (options is not null) options.WriteField(parameter.Name, Read(parameter, defaultValue));
                }
                else if (parameter.IsRequired)
                    throw new ArgumentException($"Primitive requires parameter '{parameter.Name}'.");
            }
            catch (ArgumentException exception) { throw new InvalidDataException(exception.Message, exception); }
        }

        var result = JsonSerializer.SerializeToElement(normalized);
        parameters.ValidateArguments(result);
        return result;
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

    private static object? Read(DynamicParameterDescriptor parameter, JsonElement value)
    {
        try
        {
            var decoded = ValueValidation.Read(parameter.Value, value, out var issue);
            if (issue is not null) throw new InvalidDataException($"Parameter '{parameter.Name}': {issue.Message}");
            return decoded;
        }
        catch (ArgumentException exception) { throw new InvalidDataException(exception.Message, exception); }
    }
}
