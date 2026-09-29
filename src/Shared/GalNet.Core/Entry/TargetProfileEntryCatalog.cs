namespace GalNet.Core.Entry;

/// <summary>
/// Authoring and compilation schema for one explicitly selected target profile.
/// It contains descriptor data only; it never creates modules or primitive handlers.
/// </summary>
public sealed class TargetProfileEntryCatalog : IEntryCatalog
{
    private readonly IReadOnlyDictionary<string, EntryDefinition> _definitions;

    /// <summary>
    /// Builds one target profile from optional authoring-module contributions.
    /// Runtime mounting remains independent; these tables only organize editor
    /// selection and compilation schema.
    /// </summary>
    public TargetProfileEntryCatalog(IEnumerable<IEntryModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var definitions = new Dictionary<string, EntryDefinition>(StringComparer.Ordinal);
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            if (!moduleIds.Add(module.Id))
                throw new InvalidOperationException($"Entry module catalog '{module.Id}' is already present in this target profile.");

            foreach (var entry in module.PrimitiveEntries.Values)
            {
                var definition = entry.CreateDefinition();
                if (!definitions.TryAdd(definition.Type, definition))
                    throw new InvalidOperationException($"Primitive '{definition.Type}' is already present in this target profile.");
            }

            foreach (var entry in module.CompositeEntries.Values)
            {
                var definition = entry.CreateDefinition();
                if (!definitions.TryAdd(definition.Type, definition))
                    throw new InvalidOperationException($"Entry '{definition.Type}' is already present in this target profile.");
            }
        }

        _definitions = definitions;
        Definitions = Array.AsReadOnly(definitions.Values.ToArray());
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

}
