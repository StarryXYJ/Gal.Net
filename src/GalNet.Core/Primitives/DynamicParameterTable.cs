using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace GalNet.Core.Primitives;

/// <summary>Immutable schema for one dynamically configured parameter.</summary>
public sealed class DynamicParameterDescriptor
{
    public DynamicParameterDescriptor(
        string name,
        Type valueType,
        bool isRequired = false,
        JsonElement? defaultValue = null,
        JsonElement? constraints = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parameter names cannot be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(valueType);
        if (defaultValue is { } value)
            DynamicParameterValue.Validate(value, valueType, name);
        if (constraints is { ValueKind: not JsonValueKind.Object })
            throw new ArgumentException("Parameter constraints must be a JSON object.", nameof(constraints));

        Name = name;
        ValueType = valueType;
        IsRequired = isRequired;
        DefaultValue = defaultValue?.Clone();
        Constraints = constraints?.Clone();
    }

    public string Name { get; }
    public Type ValueType { get; }
    public bool IsRequired { get; }
    public JsonElement? DefaultValue { get; }
    public JsonElement? Constraints { get; }

    public bool TryGetConstraint(string name, out JsonElement value)
    {
        if (Constraints is { } constraints && constraints.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }
}

/// <summary>A frozen, ordinally keyed dynamic parameter schema.</summary>
public sealed class DynamicParameterTable : IReadOnlyDictionary<string, DynamicParameterDescriptor>
{
    private readonly IReadOnlyDictionary<string, DynamicParameterDescriptor> _parameters;

    public DynamicParameterTable(IEnumerable<DynamicParameterDescriptor> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var copy = new Dictionary<string, DynamicParameterDescriptor>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            if (!copy.TryAdd(parameter.Name, parameter))
                throw new ArgumentException($"Parameter '{parameter.Name}' is declared more than once.", nameof(parameters));
        }
        _parameters = new ReadOnlyDictionary<string, DynamicParameterDescriptor>(copy);
    }

    public static DynamicParameterTable Empty { get; } = new([]);
    public IEnumerable<string> Keys => _parameters.Keys;
    public IEnumerable<DynamicParameterDescriptor> Values => _parameters.Values;
    public int Count => _parameters.Count;
    public DynamicParameterDescriptor this[string key] => _parameters[key];
    public bool ContainsKey(string key) => _parameters.ContainsKey(key);
    public bool TryGetValue(string key, out DynamicParameterDescriptor value) => _parameters.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, DynamicParameterDescriptor>> GetEnumerator() => _parameters.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Converts editor text to typed JSON without serializing CLR type metadata.</summary>
public static class DynamicParameterValue
{
    public static JsonElement FromEditorValue(string value, Type valueType)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        try
        {
            if (valueType == typeof(string)) return JsonSerializer.SerializeToElement(value);
            using var document = JsonDocument.Parse(value);
            var element = document.RootElement.Clone();
            Validate(element, valueType, "value");
            return element;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Expected a JSON value compatible with '{valueType.Name}', but received '{value}'.", exception);
        }
    }

    public static void Validate(JsonElement value, Type valueType, string parameterName)
    {
        try
        {
            _ = JsonSerializer.Deserialize(value.GetRawText(), valueType);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"Default value for parameter '{parameterName}' is not compatible with '{valueType.Name}'.", exception);
        }
    }

    public static string ToEditorValue(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? ""
        : value.GetRawText();
}
