using System.Text.Json;

namespace GalNet.Core.Parameters;

/// <summary>Immutable field metadata; consumers own values, labels and commit behavior.</summary>
public sealed class FieldDescriptor
{
    public FieldDescriptor(string id, ValueDescriptor value, bool isRequired = false,
        JsonElement? defaultValue = null, EditorHints? editor = null,
        string? labelKey = null, string? descriptionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(value);
        if (defaultValue is { } json && ValueValidation.Validate(value, json) is { } issue)
            throw new ArgumentException($"Field '{id}': {issue.Message}", nameof(defaultValue));
        Id = id;
        Value = value;
        IsRequired = isRequired;
        DefaultValue = defaultValue?.Clone();
        Editor = editor;
        LabelKey = labelKey;
        DescriptionKey = descriptionKey;
    }

    public string Id { get; }
    public ValueDescriptor Value { get; }
    public bool IsRequired { get; }
    public JsonElement? DefaultValue { get; }
    public EditorHints? Editor { get; }
    public string? LabelKey { get; }
    public string? DescriptionKey { get; }
}
