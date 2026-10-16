using System.Text.Json;

namespace GalNet.Core.Parameters;

/// <summary>Immutable value rules, independent of field names, defaults, editors and storage.</summary>
public sealed class ValueDescriptor
{
    public ValueDescriptor(Type valueType, bool? allowNull = null, JsonElement? constraints = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        if (constraints is { ValueKind: not JsonValueKind.Object })
            throw new ArgumentException("Value constraints must be a JSON object.", nameof(constraints));
        ValueType = valueType;
        AllowNull = allowNull ?? (valueType == typeof(JsonElement) || !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null);
        Constraints = constraints?.Clone();
        JsonOptions = jsonOptions is null ? new JsonSerializerOptions() : new JsonSerializerOptions(jsonOptions);
        JsonOptions.MakeReadOnly(populateMissingResolver: true);
        var encoding = JsonOptions.Converters.FirstOrDefault(converter => converter is IStringValueEncoding &&
            converter.CanConvert(Nullable.GetUnderlyingType(valueType) ?? valueType)) as IStringValueEncoding;
        IsStringEncoded = valueType == typeof(string) || encoding is not null;
        if (encoding is not null && !TryGetConstraint("options", out _) && !TryGetConstraint("enum", out _))
        {
            var rules = Constraints?.Deserialize<Dictionary<string, JsonElement>>() ?? new();
            rules["options"] = JsonSerializer.SerializeToElement(encoding.Choices);
            Constraints = JsonSerializer.SerializeToElement(rules);
        }
        foreach (var name in new[] { "minLength", "maxLength", "minItems", "maxItems" })
            if (TryGetConstraint(name, out var bound) && (bound.ValueKind != JsonValueKind.Number || !bound.TryGetInt32(out var length) || length < 0))
                throw new ArgumentException($"'{name}' must be a nonnegative integer.", nameof(constraints));
        foreach (var name in new[] { "minimum", "maximum" })
            if (TryGetConstraint(name, out var bound) && (bound.ValueKind != JsonValueKind.Number || !bound.TryGetDouble(out var number) || !double.IsFinite(number)))
                throw new ArgumentException($"'{name}' must be a finite number.", nameof(constraints));
        foreach (var name in new[] { "enum", "options" })
            if (TryGetConstraint(name, out var choices) && (choices.ValueKind != JsonValueKind.Array ||
                name == "options" && choices.EnumerateArray().Any(choice => choice.ValueKind != JsonValueKind.String)))
                throw new ArgumentException($"'{name}' must be an array{(name == "options" ? " of strings" : "")}.", nameof(constraints));
        if (TryGetConstraint("enum", out _) && TryGetConstraint("options", out _))
            throw new ArgumentException("Use enum or options, not both.", nameof(constraints));
        if (TryGetConstraint("options", out var legacyOptions))
            foreach (var option in legacyOptions.EnumerateArray())
            {
                try { _ = JsonSerializer.Deserialize(ValueCodec.ParseEditorText(this, option.GetString()!), ValueType, JsonOptions); }
                catch (JsonException exception) { throw new ArgumentException("An option is incompatible with the value type.", nameof(constraints), exception); }
            }
        foreach (var (minimum, maximum) in new[] { ("minimum", "maximum"), ("minLength", "maxLength"), ("minItems", "maxItems") })
            if (TryGetConstraint(minimum, out var lower) && TryGetConstraint(maximum, out var upper) && ValueValidation.CompareNumber(lower, upper) > 0)
                throw new ArgumentException($"'{minimum}' must not exceed '{maximum}'.", nameof(constraints));
    }

    public Type ValueType { get; }
    public bool AllowNull { get; }
    public JsonElement? Constraints { get; }
    public JsonSerializerOptions JsonOptions { get; }
    public bool IsStringEncoded { get; }
    public bool TryGetConstraint(string name, out JsonElement value)
    {
        if (Constraints is { } constraints && constraints.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }
}
