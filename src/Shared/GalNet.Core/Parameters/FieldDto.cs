namespace GalNet.Core.Parameters;

[AttributeUsage(AttributeTargets.Class)]
public sealed class GenerateFieldSchemaAttribute : Attribute;

/// <summary>Member type and constant initializer provide the value contract; these annotations add authoring metadata.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class FieldAttribute(int order) : Attribute
{
    public int Order { get; } = order;
    public bool Required { get; set; }
    public bool AllowNull { get; set; }
    public string? EditorId { get; set; }
    public double Step { get; set; } = double.NaN;
    public string? Format { get; set; }
    public string? UnitKey { get; set; }
    public string? LabelKey { get; set; }
    public string? DescriptionKey { get; set; }
    public double Minimum { get; set; } = double.NaN;
    public double Maximum { get; set; } = double.NaN;
    public int MaxLength { get; set; } = -1;
    public string? ValueFormat { get; set; }
    public string? DefaultJson { get; set; }
    public string[]? Choices { get; set; }
}

/// <summary>Generated member access is platform independent. Notification and submission remain host concerns.</summary>
public interface IFieldDto
{
    IReadOnlyList<FieldDescriptor> Fields { get; }
    object? ReadField(string id);
    void WriteField(string id, object? value);
}

public interface IFieldSchema<T> where T : IFieldDto
{
    static abstract IReadOnlyList<FieldDescriptor> Schema { get; }
}
