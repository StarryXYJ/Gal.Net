using System.Text.Json;

namespace GalNet.Core.Parameters;

public enum ValueStructureKind { Scalar, Object, Sequence, Map, Opaque }

/// <summary>Lazy structural metadata. Type references do not initialize their child schemas.</summary>
public sealed class ValueStructure
{
    private ValueStructure() { }
    public Type? SchemaType { get; private init; }
    public IReadOnlyList<FieldDescriptor>? Fields { get; private init; }
    public FieldDescriptor? Element { get; private init; }
    public FieldDescriptor? AdditionalField { get; private init; }
    public ValueStructureKind? Kind { get; private init; }
    public bool AllowsJsonString { get; private init; }
    public bool IgnoresCase { get; private init; }
    public bool UsesFieldIds { get; private init; }
    public string? ResolverId { get; private init; }

    public static ValueStructure ForType(Type type, bool allowsJsonString = false, bool ignoresCase = false, bool usesFieldIds = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new() { SchemaType = type, AllowsJsonString = allowsJsonString, IgnoresCase = ignoresCase, UsesFieldIds = usesFieldIds };
    }
    public static ValueStructure Object(IEnumerable<FieldDescriptor> fields, bool ignoresCase = false)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var members = fields.ToArray();
        if (members.Select(field => field.Id).Distinct(ignoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() != members.Length)
            throw new ArgumentException("Object field identities must be unique.", nameof(fields));
        return new() { Kind = ValueStructureKind.Object, Fields = Array.AsReadOnly(members), IgnoresCase = ignoresCase };
    }
    public static ValueStructure Sequence(FieldDescriptor element) => new() { Kind = ValueStructureKind.Sequence, Element = element ?? throw new ArgumentNullException(nameof(element)) };
    /// <summary>Explicitly declares additional object members and their value contract.</summary>
    public static ValueStructure OpenObject(IEnumerable<FieldDescriptor> fields, FieldDescriptor additionalField, bool ignoresCase = false)
    {
        ArgumentNullException.ThrowIfNull(additionalField);
        var shape = Object(fields, ignoresCase);
        return new() { Kind = shape.Kind, Fields = shape.Fields, IgnoresCase = shape.IgnoresCase, AdditionalField = additionalField };
    }
    /// <summary>An atomic codec value with no child semantics, including deliberately uninterpreted JSON.</summary>
    public static ValueStructure Scalar() => new() { Kind = ValueStructureKind.Scalar };
    public static ValueStructure Map(FieldDescriptor value) => new() { Kind = ValueStructureKind.Map, Element = value ?? throw new ArgumentNullException(nameof(value)) };
    public static ValueStructure Dynamic(string resolverId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resolverId);
        return new() { ResolverId = resolverId };
    }
}

/// <summary>The caller owns the frozen context and its validity; no project or UI services are required.</summary>
public interface IValueStructureProvider
{
    ValueStructure? Resolve(string id, FieldDescriptor field, JsonElement? value, PropertyPath path, object? context);
}

public sealed record ValueStructureMember(FieldDescriptor Field, string WireName);

public sealed record ResolvedValueStructure(ValueStructureKind Kind, IReadOnlyList<ValueStructureMember> Members,
    FieldDescriptor? Element = null, bool AllowsJsonString = false, bool IgnoresCase = false, ValueIssue? Issue = null)
{
    public FieldDescriptor? AdditionalField { get; init; }
}
