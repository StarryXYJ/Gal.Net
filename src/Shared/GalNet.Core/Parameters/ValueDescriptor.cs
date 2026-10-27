using GalNet.Core.Serialization;
using System.Text.Json;

namespace GalNet.Core.Parameters;

/// <summary>Immutable value rules, independent of field names, defaults, editors and storage.</summary>
public sealed class ValueDescriptor
{
    public ValueDescriptor(Type valueType, bool? allowNull = null, JsonElement? constraints = null,
        JsonSerializerOptions? jsonOptions = null, IReadOnlyList<string>? choices = null)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        if (constraints is { ValueKind: not JsonValueKind.Object })
            throw new ArgumentException("Value constraints must be a JSON object.", nameof(constraints));
        ValueType = valueType;
        AllowNull = allowNull ?? (valueType == typeof(JsonElement) || !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null);
        Constraints = constraints?.Clone();
        JsonOptions = JsonMetadata.Prepare(jsonOptions);
        var encoding = JsonOptions.Converters.FirstOrDefault(converter => converter is IStringValueEncoding &&
            converter.CanConvert(Nullable.GetUnderlyingType(valueType) ?? valueType)) as IStringValueEncoding;
        IsStringEncoded = valueType == typeof(string) || encoding is not null;
        choices ??= TryGetConstraint("enum", out _) ? null : encoding?.Choices;
        if (choices is not null)
        {
            var rules = Constraints is { } declared ? JsonCodec.Deserialize<Dictionary<string, JsonElement>>(declared) ?? new() : new();
            rules["enum"] = JsonCodec.SerializeToElement(choices.Select(choice => ValueCodec.ParseEditorText(this, choice)).ToArray());
            Constraints = JsonCodec.SerializeToElement(rules);
        }
        foreach (var name in new[] { "minLength", "maxLength", "minItems", "maxItems" })
            if (TryGetConstraint(name, out var bound) && (bound.ValueKind != JsonValueKind.Number || !bound.TryGetInt32(out var length) || length < 0))
                throw new ArgumentException($"'{name}' must be a nonnegative integer.", nameof(constraints));
        foreach (var name in new[] { "minimum", "maximum" })
            if (TryGetConstraint(name, out var bound) && (bound.ValueKind != JsonValueKind.Number || !bound.TryGetDouble(out var number) || !double.IsFinite(number)))
                throw new ArgumentException($"'{name}' must be a finite number.", nameof(constraints));
        if (TryGetConstraint("enum", out var enumValues))
        {
            if (enumValues.ValueKind != JsonValueKind.Array) throw new ArgumentException("'enum' must be an array.", nameof(constraints));
            foreach (var option in enumValues.EnumerateArray())
            {
                try { _ = JsonCodec.Deserialize(option, ValueType, JsonOptions); }
                catch (JsonException exception) { throw new ArgumentException("An option is incompatible with the value type.", nameof(constraints), exception); }
            }
        }
        foreach (var (minimum, maximum) in new[] { ("minimum", "maximum"), ("minLength", "maxLength"), ("minItems", "maxItems") })
            if (TryGetConstraint(minimum, out var lower) && TryGetConstraint(maximum, out var upper) && ValueValidation.CompareNumber(lower, upper) > 0)
                throw new ArgumentException($"'{minimum}' must not exceed '{maximum}'.", nameof(constraints));
    }

    public Type ValueType { get; }
    public bool AllowNull { get; }
    public JsonElement? Constraints { get; }
    public JsonSerializerOptions JsonOptions { get; private init; }
    public bool IsStringEncoded { get; }
    public ValueStructure? Structure { get; private init; }
    public Func<IReadOnlyList<FieldDescriptor>>? SchemaProvider { get; private init; }

    public ValueDescriptor WithSchemaProvider(Func<IReadOnlyList<FieldDescriptor>> provider) =>
        new(ValueType, AllowNull, Constraints, JsonOptions) { Structure = Structure, SchemaProvider = provider };

    /// <summary>Describe structured wire data without changing its codec or the existing constructor ABI.</summary>
    public ValueDescriptor WithStructure(ValueStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        return new(ValueType, AllowNull, Constraints, JsonOptions) { Structure = structure, SchemaProvider = SchemaProvider, JsonOptions = JsonOptions };
    }

    public ValueDescriptor WithJsonOptions(JsonSerializerOptions options) =>
        new(ValueType, AllowNull, Constraints, options) { Structure = Structure, SchemaProvider = SchemaProvider, JsonOptions = JsonMetadata.Prepare(options) };

    public bool TryGetConstraint(string name, out JsonElement value)
    {
        if (Constraints is { } constraints && constraints.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }
}
