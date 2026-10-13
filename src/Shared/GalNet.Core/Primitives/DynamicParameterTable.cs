using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using GalNet.Core.Parameters;

namespace GalNet.Core.Primitives;

/// <summary>Immutable schema for one dynamically configured parameter.</summary>
public sealed class DynamicParameterDescriptor
{
    public DynamicParameterDescriptor(
        string name,
        Type valueType,
        bool isRequired = false,
        JsonElement? defaultValue = null,
        JsonElement? constraints = null,
        bool? allowNull = null)
        : this(name, new ValueDescriptor(valueType, allowNull, constraints), isRequired, defaultValue) { }

    public DynamicParameterDescriptor(string name, ValueDescriptor value, bool isRequired = false, JsonElement? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parameter names cannot be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        IsRequired = isRequired;
        DefaultValue = defaultValue?.Clone();
        Value = value;
        if (DefaultValue is { } json) Validate(json);
    }

    public string Name { get; }
    public Type ValueType => Value.ValueType;
    public bool IsRequired { get; }
    public ValueDescriptor Value { get; }
    public bool AllowNull => Value.AllowNull;
    public JsonElement? DefaultValue { get; }
    public JsonElement? Constraints => Value.Constraints;

    public void Validate(JsonElement value)
    {
        if (ValueValidation.Validate(Value, value) is { } issue)
            throw new ArgumentException($"Parameter '{Name}': {issue.Message}");
    }

    public bool TryGetConstraint(string name, out JsonElement value) => Value.TryGetConstraint(name, out value);
}

/// <summary>A frozen, ordinally keyed dynamic parameter schema.</summary>
public sealed class DynamicParameterTable : IReadOnlyDictionary<string, DynamicParameterDescriptor>
{
    private readonly IReadOnlyDictionary<string, DynamicParameterDescriptor> _parameters;
    private readonly Action<JsonElement>? _validateArguments;

    public DynamicParameterTable(IEnumerable<DynamicParameterDescriptor> parameters, Action<JsonElement>? validateArguments = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _validateArguments = validateArguments;
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
    public void ValidateArguments(JsonElement arguments) => _validateArguments?.Invoke(arguments);
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
            return ValueCodec.FromEditorText(new ValueDescriptor(valueType), value);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Expected a JSON value compatible with '{valueType.Name}', but received '{value}'.", exception);
        }
    }

    public static void Validate(JsonElement value, Type valueType, string parameterName)
    {
        if (ValueValidation.Validate(new ValueDescriptor(valueType), value) is { } issue)
            throw new ArgumentException($"Parameter '{parameterName}': {issue.Message}");
    }

    public static string ToEditorValue(JsonElement value) => ValueCodec.ToEditorText(value);
}
