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
        bool? allowNull = null,
        EditorHints? editor = null)
        : this(name, new ValueDescriptor(valueType, allowNull, constraints), isRequired, defaultValue, editor) { }

    public DynamicParameterDescriptor(string name, ValueDescriptor value, bool isRequired = false,
        JsonElement? defaultValue = null, EditorHints? editor = null)
        : this(new FieldDescriptor(name, value, isRequired, defaultValue, editor)) { }

    public DynamicParameterDescriptor(FieldDescriptor field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Field = field;
    }

    public FieldDescriptor Field { get; }
    public string Name => Field.Id;
    public Type ValueType => Value.ValueType;
    public bool IsRequired => Field.IsRequired;
    public ValueDescriptor Value => Field.Value;
    public bool AllowNull => Value.AllowNull;
    public JsonElement? DefaultValue => Field.DefaultValue;
    public EditorHints? Editor => Field.Editor;
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
    private readonly Action<JsonElement>? _validatePartialArguments;

    public DynamicParameterTable(IEnumerable<DynamicParameterDescriptor> parameters, Action<JsonElement>? validateArguments = null,
        Action<JsonElement>? validatePartialArguments = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _validateArguments = validateArguments;
        _validatePartialArguments = validatePartialArguments ?? validateArguments;
        var copy = new Dictionary<string, DynamicParameterDescriptor>(StringComparer.Ordinal);
        var fields = new List<FieldDescriptor>();
        foreach (var parameter in parameters)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            if (!copy.TryAdd(parameter.Name, parameter))
                throw new ArgumentException($"Parameter '{parameter.Name}' is declared more than once.", nameof(parameters));
            fields.Add(parameter.Field);
        }
        _parameters = new ReadOnlyDictionary<string, DynamicParameterDescriptor>(copy);
        Fields = fields.AsReadOnly();
    }

    public static DynamicParameterTable Empty { get; } = new([]);
    public IReadOnlyList<FieldDescriptor> Fields { get; }
    public IEnumerable<string> Keys => _parameters.Keys;
    public IEnumerable<DynamicParameterDescriptor> Values => _parameters.Values;
    public int Count => _parameters.Count;
    public DynamicParameterDescriptor this[string key] => _parameters[key];
    public bool ContainsKey(string key) => _parameters.ContainsKey(key);
    public void ValidateArguments(JsonElement arguments) => _validateArguments?.Invoke(arguments);
    /// <summary>Authoring may omit unfinished fields and defaults; full compilation still uses ValidateArguments.</summary>
    public void ValidatePartialArguments(JsonElement arguments) => _validatePartialArguments?.Invoke(arguments);
    public bool TryGetValue(string key, out DynamicParameterDescriptor value) => _parameters.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, DynamicParameterDescriptor>> GetEnumerator() => _parameters.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
