using System.Text.Json;
using GalNet.Core.Primitives;

namespace GalNet.Core.Entry;

/// <summary>
/// Authoring and compilation schema for one explicitly selected target profile.
/// It contains descriptor data only; it never creates modules or primitive handlers.
/// </summary>
public sealed class TargetProfileEntryCatalog : IEntryCatalog
{
    private readonly IReadOnlyDictionary<string, EntryDefinition> _definitions;

    public TargetProfileEntryCatalog(
        IEnumerable<PrimitiveDescriptor> primitiveDescriptors,
        IEnumerable<EntryDefinition>? nonPrimitiveDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(primitiveDescriptors);

        var definitions = new Dictionary<string, EntryDefinition>(StringComparer.Ordinal);
        foreach (var descriptor in primitiveDescriptors)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ValidatePrimitiveDescriptor(descriptor);
            var definition = CreatePrimitiveDefinition(descriptor);
            if (!definitions.TryAdd(definition.Type, definition))
                throw new InvalidOperationException($"Primitive '{definition.Type}' is already present in this target profile.");
        }

        foreach (var definition in nonPrimitiveDefinitions ?? [])
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.Kind != EntryKind.NonPrimitive)
                throw new ArgumentException("Target-profile extensions may only contribute non-primitive entries.", nameof(nonPrimitiveDefinitions));
            if (!definitions.TryAdd(definition.Type, definition))
                throw new InvalidOperationException($"Entry '{definition.Type}' is already present in this target profile.");
        }

        _definitions = definitions;
        Definitions = definitions.Values.ToArray();
    }

    /// <summary>
    /// Builds authoring schema from the same frozen descriptor tables that a Game
    /// Scope mounts. The modules are not dispatched or otherwise executed here.
    /// </summary>
    public static TargetProfileEntryCatalog FromModules(
        IEnumerable<IPrimitiveModule> modules,
        IEnumerable<EntryDefinition>? nonPrimitiveDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var descriptors = new List<PrimitiveDescriptor>();
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            ValidateModulePrefix(module.Prefix);
            if (!prefixes.Add(module.Prefix))
                throw new InvalidOperationException($"Primitive module prefix '{module.Prefix}' is already present in this target profile.");
            foreach (var descriptor in module.Descriptors ?? throw new InvalidOperationException($"Primitive module '{module.Prefix}' has no descriptor collection."))
            {
                ArgumentNullException.ThrowIfNull(descriptor);
                if (!descriptor.TypeId.StartsWith($"{module.Prefix}.", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Primitive '{descriptor.TypeId}' does not belong to module '{module.Prefix}'.");
                descriptors.Add(descriptor);
            }
        }
        return new TargetProfileEntryCatalog(descriptors, nonPrimitiveDefinitions);
    }

    public IReadOnlyList<EntryDefinition> Definitions { get; }

    public bool TryGet(string type, out EntryDefinition definition) => _definitions.TryGetValue(type, out definition!);

    public EntryDefinition Get(string type) => TryGet(type, out var definition)
        ? definition
        : throw new InvalidDataException($"Entry type '{type}' is not enabled by this target profile.");

    public Entry Create(string type, int id = 0, string condition = "", IReadOnlyDictionary<string, string>? values = null)
    {
        var definition = Get(type);
        var entry = definition.Factory();
        entry.Id = id;
        entry.Condition = condition;
        foreach (var (name, value) in definition.Defaults)
            entry.Values[name] = value;
        if (values is null) return entry;

        foreach (var (name, value) in values)
            if (definition.Parameters.ContainsKey(name)) entry.Values[name] = value;
        return entry;
    }

    private static EntryDefinition CreatePrimitiveDefinition(PrimitiveDescriptor descriptor)
    {
        var parameters = new Dictionary<string, EntryParameterType>(StringComparer.Ordinal);
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var parameter in descriptor.Parameters.Values)
        {
            if (!parameters.TryAdd(parameter.Name, ToEntryParameterType(parameter)))
                throw new ArgumentException($"Primitive '{descriptor.TypeId}' declares parameter '{parameter.Name}' more than once.", nameof(descriptor));
            if (parameter.DefaultValue is { } defaultValue)
                defaults.Add(parameter.Name, ToPersistedValue(defaultValue));
            if (parameter.ValueType == typeof(bool))
                options.Add(parameter.Name, ["true", "false"]);
        }

        return new EntryDefinition(
            descriptor.TypeId,
            GetCategory(descriptor.TypeId),
            () => new AuthoringPrimitiveEntry(descriptor.TypeId),
            parameters,
            defaults,
            options,
            EntryKind.Primitive,
            descriptor,
            descriptor.Parameters);
    }

    private static EntryParameterType ToEntryParameterType(DynamicParameterDescriptor parameter) =>
        EntrySchema.GetEditorType(parameter);

    private static string ToPersistedValue(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? ""
        : value.GetRawText();

    private static string GetCategory(string typeId) => typeId[..typeId.IndexOf('.')];

    private static void ValidatePrimitiveDescriptor(PrimitiveDescriptor descriptor)
    {
        var separator = descriptor.TypeId.IndexOf('.');
        if (string.IsNullOrWhiteSpace(descriptor.TypeId) || separator <= 0 || separator == descriptor.TypeId.Length - 1 ||
            !string.Equals(descriptor.TypeId[..separator], descriptor.TypeId[..separator].ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("Primitive type IDs must have a lowercase module prefix and a non-empty command.", nameof(descriptor));
        if (descriptor.Parameters is null)
            throw new ArgumentException($"Primitive '{descriptor.TypeId}' has no parameter collection.", nameof(descriptor));
    }

    private static void ValidateModulePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Contains('.') ||
            !string.Equals(prefix, prefix.ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("Primitive module prefixes must be non-empty, lowercase, dot-free identifiers.", nameof(prefix));
    }
}
