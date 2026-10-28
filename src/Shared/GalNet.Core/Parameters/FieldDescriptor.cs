using System.Text.Json;

namespace GalNet.Core.Parameters;

/// <summary>Immutable field metadata; consumers own values, labels and commit behavior.</summary>
public sealed class FieldDescriptor
{
    public FieldDescriptor(string id, ValueDescriptor value, bool isRequired = false,
        JsonElement? defaultValue = null, EditorHints? editor = null,
        string? labelKey = null, string? descriptionKey = null,
        FieldValueSemantic semantic = FieldValueSemantic.None, string? resourceTypeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(semantic) || semantic != FieldValueSemantic.None && value.ValueType != typeof(string))
            throw new ArgumentException("Game text and character semantics require a string field.", nameof(semantic));
        if (defaultValue is { } json && ValueValidation.Validate(value, json) is { } issue)
            throw new ArgumentException($"Field '{id}': {issue.Message}", nameof(defaultValue));
        Id = id;
        Value = value;
        IsRequired = isRequired;
        DefaultValue = defaultValue?.Clone();
        Editor = editor;
        LabelKey = labelKey;
        DescriptionKey = descriptionKey;
        Semantic = semantic;
        if (resourceTypeId is not null && value.ValueType != typeof(string))
            throw new ArgumentException("Resource references require a string field.", nameof(resourceTypeId));
        ResourceTypeId = resourceTypeId;
    }

    public string Id { get; }
    public ValueDescriptor Value { get; }
    public bool IsRequired { get; }
    public JsonElement? DefaultValue { get; }
    public EditorHints? Editor { get; }
    public string? LabelKey { get; }
    public string? DescriptionKey { get; }
    public FieldValueSemantic Semantic { get; }
    public string? ResourceTypeId { get; }
    public string? MemberName { get; private init; }
    public string? JsonPropertyName { get; private init; }

    public FieldDescriptor WithMember(string memberName, string? jsonPropertyName = null) =>
        new(Id, Value, IsRequired, DefaultValue, Editor, LabelKey, DescriptionKey, Semantic, ResourceTypeId)
        { MemberName = memberName, JsonPropertyName = jsonPropertyName };
}
