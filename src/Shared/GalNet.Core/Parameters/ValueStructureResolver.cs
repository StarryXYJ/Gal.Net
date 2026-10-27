using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GalNet.Core.Parameters;

/// <summary>Caller-scoped metadata cache. Never constructs a DTO or reads its instance properties.</summary>
public sealed class ValueStructureResolver : IDisposable
{
    private readonly Dictionary<(Type Type, JsonSerializerOptions Options, ValueStructure? Structure), ResolvedValueStructure> _cache = [];
    private bool _disposed;
    public ResolvedValueStructure Resolve(FieldDescriptor field, JsonElement? value = null, PropertyPath? path = null,
        IValueStructureProvider? provider = null, object? context = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var descriptor = field.Value;
        var declaration = descriptor.Structure;
        if (declaration?.ResolverId is { } id)
        {
            declaration = provider?.Resolve(id, field, value, path ?? PropertyPath.Empty, context);
            if (declaration is null || declaration.ResolverId is not null) return Opaque("Dynamic structure is unavailable.");
            return ResolveCore(descriptor, declaration);
        }
        var key = (descriptor.ValueType, descriptor.JsonOptions, declaration);
        if (!_cache.TryGetValue(key, out var result)) _cache.Add(key, result = ResolveCore(descriptor, declaration));
        return result;
    }
    private static ResolvedValueStructure Opaque(string message) => new(ValueStructureKind.Opaque, [], Issue: new("field.opaque", message));
    private static ResolvedValueStructure ResolveCore(ValueDescriptor descriptor, ValueStructure? declaration)
    {
        try
        {
            if (declaration?.Kind is { } explicitKind)
                return new(explicitKind, Array.AsReadOnly(declaration.Fields?.Select(field => new ValueStructureMember(Inherit(field, descriptor.JsonOptions), field.Id)).ToArray() ?? []),
                    declaration.Element is { } elementField ? Inherit(elementField, descriptor.JsonOptions) : null,
                    declaration.AllowsJsonString, declaration.IgnoresCase);
            var type = Nullable.GetUnderlyingType(declaration?.SchemaType ?? descriptor.ValueType) ?? declaration?.SchemaType ?? descriptor.ValueType;
            if (declaration is null && descriptor.IsStringEncoded && IsScalar(type)) return new(ValueStructureKind.Scalar, []);
            if (declaration is null && type == typeof(JsonElement)) return Opaque("Structured value has no traversable field schema.");
            var info = descriptor.JsonOptions.GetTypeInfo(type);
            if (info.Kind == JsonTypeInfoKind.None)
                return declaration?.SchemaType is not null
                    ? new(ValueStructureKind.Opaque, [], Issue: new("schema.invalid", "The declared structure type does not expose an object or collection contract."))
                    : IsScalar(type)
                    ? new(ValueStructureKind.Scalar, []) : Opaque("The value codec does not expose a traversable structure.");
            var element = ElementType(type);
            if (info.Kind == JsonTypeInfoKind.Enumerable && element is not null)
                return new(ValueStructureKind.Sequence, [], new("element", new(element, jsonOptions: descriptor.JsonOptions)), AllowsJsonString: declaration?.AllowsJsonString == true);
            var map = MapType(type);
            if (info.Kind == JsonTypeInfoKind.Dictionary && map is not null)
                return new(ValueStructureKind.Map, [], new("value", new(map, jsonOptions: descriptor.JsonOptions)), AllowsJsonString: declaration?.AllowsJsonString == true);
            if (!typeof(IFieldDto).IsAssignableFrom(type) || info.Kind != JsonTypeInfoKind.Object)
                return Opaque("Structured value has no traversable field schema.");
            if (type.GetProperty("Schema", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) is not IReadOnlyList<FieldDescriptor> fields)
                return new(ValueStructureKind.Opaque, [], Issue: new("schema.unavailable", "Nested DTO has no readable schema."));
            var members = fields.Select(field => MapMember(field, type, descriptor.JsonOptions, info, declaration?.UsesFieldIds == true)).ToArray();
            var comparer = declaration?.IgnoresCase == true || descriptor.JsonOptions.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (members.Select(member => member.WireName).Distinct(comparer).Count() != members.Length)
                throw new InvalidOperationException("Schema members have ambiguous wire names.");
            return new(ValueStructureKind.Object, Array.AsReadOnly(members), AllowsJsonString: declaration?.AllowsJsonString == true,
                IgnoresCase: comparer == StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or TargetInvocationException or TypeInitializationException)
        { return new(ValueStructureKind.Opaque, [], Issue: new("schema.invalid", error.InnerException?.Message ?? error.Message)); }
    }
    private static ValueStructureMember MapMember(FieldDescriptor field, Type type, JsonSerializerOptions parent, JsonTypeInfo info, bool usesFieldIds)
    {
        var source = type.GetMembers(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(member => member is PropertyInfo or FieldInfo
            && member.GetCustomAttribute<FieldAttribute>() is not null
            && (member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(member.Name)) == field.Id);
        var wire = info.Properties.FirstOrDefault(property => property.AttributeProvider is MemberInfo member && member.Name == source?.Name);
        // Handwritten IFieldDto schemas may describe wire members without Field annotations.
        wire ??= info.Properties.FirstOrDefault(property => property.Name == field.Id);
        if (source is not null && wire is null) throw new InvalidOperationException($"Field '{field.Id}' is absent from the wire contract.");
        return new(Inherit(field, parent), usesFieldIds ? field.Id : wire?.Name ?? field.Id);
    }
    private static FieldDescriptor Inherit(FieldDescriptor field, JsonSerializerOptions parent)
    {
        var options = parent;
        if (field.Value.JsonOptions.Converters.Count > 0 &&
            !parent.Converters.Take(field.Value.JsonOptions.Converters.Count).SequenceEqual(field.Value.JsonOptions.Converters))
        {
            options = new(parent);
            foreach (var converter in field.Value.JsonOptions.Converters.Reverse()) options.Converters.Insert(0, converter);
        }
        var value = field.Value.WithJsonOptions(options);
        return new(field.Id, value, field.IsRequired, field.DefaultValue, field.Editor, field.LabelKey, field.DescriptionKey, field.Semantic);
    }
    public static Type? ElementType(Type type) => type.IsArray && type.GetArrayRank() == 1 ? type.GetElementType()
        : type.IsGenericType && type.GetGenericTypeDefinition() is var generic &&
          (generic == typeof(List<>) || generic == typeof(IList<>) || generic == typeof(IReadOnlyList<>) || generic == typeof(IEnumerable<>)) ? type.GetGenericArguments()[0] : null;
    public static Type? MapType(Type type) => type.IsGenericType && type.GetGenericArguments() is var arguments && arguments.Length == 2 && arguments[0] == typeof(string)
        && type.GetGenericTypeDefinition() is var generic && (generic == typeof(Dictionary<,>) || generic == typeof(IDictionary<,>) || generic == typeof(IReadOnlyDictionary<,>)) ? arguments[1] : null;
    private static bool IsScalar(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
        || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(Uri) || type == typeof(Version) || type == typeof(byte[]);
    public void Dispose() { _disposed = true; _cache.Clear(); }
}
