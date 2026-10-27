using GalNet.Core.Serialization;
using System.Text.Json;

namespace GalNet.Core.Parameters;

public sealed record ValueIssue(string Code, string Message, string? Path = null);

/// <summary>Shared validation without command, setting or persistence-specific errors.</summary>
public static class ValueValidation
{
    public static ValueIssue? ValidateValue(ValueDescriptor descriptor, object? value)
    {
        if (value is double number && !double.IsFinite(number) || value is float single && !float.IsFinite(single))
            return new("value.finite", "Value must be finite.");
        try
        {
            return Validate(descriptor, JsonCodec.SerializeToElement(value, descriptor.ValueType, descriptor.JsonOptions));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new("value.type", $"Value is not compatible with '{descriptor.ValueType.Name}'.");
        }
    }

    public static ValueIssue? Validate(ValueDescriptor descriptor, JsonElement value)
    {
        _ = Read(descriptor, value, out var issue);
        return issue;
    }

    internal static object? Read(ValueDescriptor descriptor, JsonElement value, out ValueIssue? issue)
    {
        issue = null;
        if (value.ValueKind == JsonValueKind.Null && !descriptor.AllowNull)
        {
            issue = new("value.null", "Value must not be null.");
            return null;
        }
        if (value.ValueKind == JsonValueKind.Null)
            return descriptor.ValueType == typeof(JsonElement) ? value.Clone() : null;
        object? result;
        try { result = JsonCodec.Deserialize(value, descriptor.ValueType, descriptor.JsonOptions); }
        catch (JsonException exception)
        {
            issue = new("value.type", $"Value is not compatible with '{descriptor.ValueType.Name}'.", exception.Path);
            return null;
        }
        if (result is double number && !double.IsFinite(number) || result is float single && !float.IsFinite(single))
            issue = new("value.finite", "Value must be finite.");
        else if (value.ValueKind == JsonValueKind.Number &&
            (descriptor.TryGetConstraint("minimum", out var minimum) && CompareNumber(value, minimum) < 0 ||
             descriptor.TryGetConstraint("maximum", out var maximum) && CompareNumber(value, maximum) > 0))
            issue = new("value.range", "Value is outside its supported range.");
        else if (value.ValueKind == JsonValueKind.String && OutsideLength(descriptor, "minLength", "maxLength", CharacterCount(value.GetString()!)))
            issue = new("value.length", "Value is outside its supported length.");
        else if (value.ValueKind == JsonValueKind.Array && OutsideLength(descriptor, "minItems", "maxItems", value.GetArrayLength()))
            issue = new("value.items", "Array is outside its supported length.");
        else if (descriptor.TryGetConstraint("enum", out var choices) && !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value)))
            issue = new("value.option", "Value has an unsupported value.");
        return result;
    }

    internal static int CompareNumber(JsonElement left, JsonElement right) =>
        left.TryGetDecimal(out var first) && right.TryGetDecimal(out var second)
            ? first.CompareTo(second) : left.GetDouble().CompareTo(right.GetDouble());

    private static bool OutsideLength(ValueDescriptor descriptor, string minName, string maxName, int length) =>
        descriptor.TryGetConstraint(minName, out var minimum) && length < minimum.GetInt32() ||
        descriptor.TryGetConstraint(maxName, out var maximum) && length > maximum.GetInt32();

    private static int CharacterCount(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes()) count++;
        return count;
    }
}
