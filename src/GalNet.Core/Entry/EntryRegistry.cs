namespace GalNet.Core.Entry;

/// <summary>Core-owned registry for authoring-only non-primitive expansions.</summary>
public static class EntryRegistry
{
    private static readonly IReadOnlyDictionary<string, EntryDefinition> DefinitionsByType = BuildDefinitions();
    /// <summary>Non-primitive definitions that Core expands during compilation.</summary>
    public static IReadOnlyList<EntryDefinition> Definitions { get; } = DefinitionsByType.Values.ToArray();

    public static bool TryGet(string type, out EntryDefinition definition) => DefinitionsByType.TryGetValue(type, out definition!);

    public static EntryDefinition Get(string type) => TryGet(type, out var definition)
        ? definition
        : throw new InvalidDataException($"Unknown entry type '{type}'.");

    /// <summary>Creates an entry and applies the registered default parameter values before supplied values.</summary>
    /// <param name="type">Registered entry type identifier.</param>
    /// <param name="id">Identifier assigned to the created entry.</param>
    /// <param name="condition">Optional execution condition expression.</param>
    /// <param name="values">Persisted parameter values; unsupported names are ignored except for <c>variable.set</c>.</param>
    /// <returns>A concrete entry matching the registered schema.</returns>
    /// <exception cref="InvalidDataException">Thrown when <paramref name="type"/> is unknown.</exception>
    public static Entry Create(string type, int id = 0, string condition = "", IReadOnlyDictionary<string, string>? values = null)
    {
        var definition = Get(type);
        var entry = definition.Factory();
        entry.Id = id;
        entry.Condition = condition;
        foreach (var (name, value) in definition.Defaults)
            entry.Values[name] = value;
        if (values is not null)
        {
            foreach (var (name, value) in values)
                if (definition.Parameters.ContainsKey(name))
                    entry.Values[name] = value;
        }
        return entry;
    }

    private static IReadOnlyDictionary<string, EntryDefinition> BuildDefinitions()
    {
        var definitions = new[]
        {
            Define(CrossFadeTransitionEntry.TypeId, "Transition", () => new CrossFadeTransitionEntry(), CrossFadeTransitionEntry.ParameterTypes, CrossFadeTransitionEntry.DefaultValues, CrossFadeTransitionEntry.ParameterOptions, EntryKind.NonPrimitive),
            Define(SlideTransitionEntry.TypeId, "Transition", () => new SlideTransitionEntry(), SlideTransitionEntry.ParameterTypes, SlideTransitionEntry.DefaultValues, SlideTransitionEntry.ParameterOptions, EntryKind.NonPrimitive),
            Define(BlindsTransitionEntry.TypeId, "Transition", () => new BlindsTransitionEntry(), BlindsTransitionEntry.ParameterTypes, BlindsTransitionEntry.DefaultValues, BlindsTransitionEntry.ParameterOptions, EntryKind.NonPrimitive),
            Define(BlackFadeTransitionEntry.TypeId, "Transition", () => new BlackFadeTransitionEntry(), BlackFadeTransitionEntry.ParameterTypes, BlackFadeTransitionEntry.DefaultValues, BlackFadeTransitionEntry.ParameterOptions, EntryKind.NonPrimitive),
            Define(WhiteFadeTransitionEntry.TypeId, "Transition", () => new WhiteFadeTransitionEntry(), WhiteFadeTransitionEntry.ParameterTypes, WhiteFadeTransitionEntry.DefaultValues, WhiteFadeTransitionEntry.ParameterOptions, EntryKind.NonPrimitive),
            Define(ColorFadeTransitionEntry.TypeId, "Transition", () => new ColorFadeTransitionEntry(), ColorFadeTransitionEntry.ParameterTypes, ColorFadeTransitionEntry.DefaultValues, ColorFadeTransitionEntry.ParameterOptions, EntryKind.NonPrimitive)
        };
        return new Dictionary<string, EntryDefinition>(definitions.ToDictionary(x => x.Type), StringComparer.Ordinal);
    }

    private static EntryDefinition Define(
        string type,
        string category,
        Func<Entry> factory,
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? options = null,
        EntryKind kind = EntryKind.NonPrimitive) =>
        new(type, category, factory, parameters, defaults ?? new Dictionary<string, string>(), options ?? new Dictionary<string, IReadOnlyList<string>>(), kind);
}
