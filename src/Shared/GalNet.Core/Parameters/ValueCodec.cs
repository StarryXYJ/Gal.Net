using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace GalNet.Core.Parameters;

/// <summary>Typed JSON conversion shared by authoring, settings and optional command/variable consumers.</summary>
public static class ValueCodec
{
    /// <summary>Serialize DTO members with their field codecs; undefined JSON fields remain omitted for normalization.</summary>
    public static JsonElement SerializeFields(IFieldDto data, bool omitNullValues = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in data.Fields)
        {
            var value = data.ReadField(field.Id);
            if (value is JsonElement { ValueKind: JsonValueKind.Undefined } || omitNullValues && value is null) continue;
            values.Add(field.Id, Serialize(field.Value, value));
        }
        return JsonSerializer.SerializeToElement(values);
    }

    public static T DeserializeFields<T>(IReadOnlyList<FieldDescriptor> fields, JsonElement arguments) where T : IFieldDto, new()
    {
        var data = new T();
        foreach (var field in fields)
            if (arguments.TryGetProperty(field.Id, out var value))
                data.WriteField(field.Id, Deserialize(field.Value, value));
        return data;
    }

    public static JsonElement ParseDefault(ValueDescriptor descriptor, string json)
    {
        var value = JsonDocumentValue(json);
        _ = Deserialize(descriptor, value);
        return value;
    }
    public static object? Deserialize(ValueDescriptor descriptor, JsonElement value)
    {
        var result = ValueValidation.Read(descriptor, value, out var issue);
        if (issue is not null) throw new ArgumentException(issue.Message, issue.Path);
        return result;
    }

    public static JsonElement Serialize(ValueDescriptor descriptor, object? value)
    {
        var json = JsonSerializer.SerializeToElement(value, descriptor.ValueType, descriptor.JsonOptions);
        _ = Deserialize(descriptor, json);
        return json;
    }

    public static JsonElement FromEditorText(ValueDescriptor descriptor, string text)
    {
        var value = ParseEditorText(descriptor, text);
        _ = Deserialize(descriptor, value);
        return value;
    }

    /// <summary>Parse a draft without enforcing constraints; validate before committing it.</summary>
    public static JsonElement ParseEditorText(ValueDescriptor descriptor, string text) =>
        descriptor.IsStringEncoded ? JsonSerializer.SerializeToElement(text) : JsonDocumentValue(NormalizeEditorNumber(descriptor, text));

    private static bool IsFractional(ValueDescriptor descriptor) =>
        (Nullable.GetUnderlyingType(descriptor.ValueType) ?? descriptor.ValueType) is var type &&
        (type == typeof(float) || type == typeof(double) || type == typeof(decimal));

    private static string NormalizeEditorNumber(ValueDescriptor descriptor, string text)
    {
        if (!IsFractional(descriptor)) return text;
        var number = text.Trim();
        if (number.StartsWith('.')) return "0" + number;
        if (number.StartsWith("-.", StringComparison.Ordinal)) return "-0" + number[1..];
        return text;
    }

    /// <summary>Transient numeric prefixes may remain neutral until the user confirms the input.</summary>
    public static bool IsIncompleteEditorNumber(ValueDescriptor descriptor, string text)
    {
        if (descriptor.IsStringEncoded) return false;
        var type = Nullable.GetUnderlyingType(descriptor.ValueType) ?? descriptor.ValueType;
        if (!IsFractional(descriptor)) return type.IsPrimitive && type != typeof(bool) && type != typeof(char) && text.Trim() == "-";
        var number = text.Trim();
        if (number is "-" or "." or "-.") return true;
        if (number.EndsWith('.') && double.TryParse(number[..^1], System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out var integerPart) && double.IsFinite(integerPart)) return true;
        var exponent = number.IndexOfAny(['e', 'E']);
        return exponent > 0 && number[(exponent + 1)..] is "" or "-" or "+" &&
            double.TryParse(NormalizeEditorNumber(descriptor, number[..exponent]), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mantissa) && double.IsFinite(mantissa);
    }

    private static JsonElement JsonDocumentValue(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public static string ToEditorText(ValueDescriptor descriptor, JsonElement value) =>
        descriptor.IsStringEncoded && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : value.GetRawText();

    public static JsonElement Describe(ValueDescriptor descriptor, string? referencePrefix = null)
    {
        var schemaOptions = descriptor.JsonOptions.Converters.Any(converter => converter is IStringValueEncoding && converter.CanConvert(typeof(string)))
            ? JsonSerializerOptions.Default : descriptor.JsonOptions;
        var exported = schemaOptions.GetJsonSchemaAsNode(descriptor.IsStringEncoded ? typeof(string) : descriptor.ValueType);
        var prefix = referencePrefix ?? "#";
        RebaseReferences(exported, prefix + (descriptor.AllowNull ? "/anyOf/0/allOf/0" : "/allOf/0"));
        // Root rules must not alter the nullability or constraints of recursively referenced child types.
        var schema = new JsonObject { ["allOf"] = new JsonArray(exported), ["not"] = new JsonObject { ["type"] = "null" } };
        if (descriptor.Constraints is { } constraints)
            foreach (var constraint in constraints.EnumerateObject())
                if (constraint.Name is "minimum" or "maximum" or "minLength" or "maxLength" or "minItems" or "maxItems" or "enum")
                    schema[constraint.Name] = JsonNode.Parse(constraint.Value.GetRawText());
        return JsonSerializer.SerializeToElement(descriptor.AllowNull
            ? new JsonObject { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) }
            : schema);
    }

    private static void RebaseReferences(JsonNode? node, string prefix)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var path) && path.StartsWith('#'))
                obj["$ref"] = prefix + path[1..];
            foreach (var child in obj) RebaseReferences(child.Value, prefix);
        }
        else if (node is JsonArray array)
            foreach (var child in array) RebaseReferences(child, prefix);
    }
}
