using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;

namespace GalNet.Primitives.Builtins;

/// <summary>
/// The built-in authoring catalog. This is the single source of primitive schema
/// metadata for the compiler and editor; Core contains only the generic contracts.
/// </summary>
public static class BuiltinEntryCatalog
{
    private static readonly IReadOnlyDictionary<string, EntryDefinition> DefinitionsByType = BuildDefinitions();

    public static IReadOnlyList<EntryDefinition> Definitions { get; } = DefinitionsByType.Values.ToArray();
    public static IEntryCatalog Instance { get; } = new Catalog();

    public static bool TryGet(string type, out EntryDefinition definition) =>
        DefinitionsByType.TryGetValue(type, out definition!);

    public static EntryDefinition Get(string type) => TryGet(type, out var definition)
        ? definition
        : throw new InvalidDataException($"Unknown entry type '{type}'.");

    public static Entry Create(string type, int id = 0, string condition = "", IReadOnlyDictionary<string, string>? values = null)
    {
        var definition = Get(type);
        var entry = definition.Factory();
        entry.Id = id;
        entry.Condition = condition;
        foreach (var (name, value) in definition.Defaults)
            entry.Values[name] = value;
        if (values is null) return entry;

        if (type == SetVariableEntry.TypeId && values.Keys.Any(name => !definition.Parameters.ContainsKey(name)))
            throw new InvalidDataException("The variable.set entry only accepts 'target' and 'expression'.");
        foreach (var (name, value) in values)
            if (definition.Parameters.ContainsKey(name)) entry.Values[name] = value;
        return entry;
    }

    private static IReadOnlyDictionary<string, EntryDefinition> BuildDefinitions()
    {
        var primitives = new[]
        {
            Primitive<TextEntry>("Dialogue", TextEntry.ParameterTypes, TextEntry.DefaultValues),
            Primitive<ShowDialogueEntry>("Dialogue", ShowDialogueEntry.ParameterTypes),
            Primitive<HideDialogueEntry>("Dialogue", HideDialogueEntry.ParameterTypes),
            Primitive<ShowLayerEntry>("Layer", ShowLayerEntry.ParameterTypes, ShowLayerEntry.DefaultValues, ShowLayerEntry.ParameterOptions),
            Primitive<ShowColorLayerEntry>("Layer", ShowColorLayerEntry.ParameterTypes, ShowColorLayerEntry.DefaultValues),
            Primitive<HideLayerEntry>("Layer", HideLayerEntry.ParameterTypes),
            Primitive<MoveLayerEntry>("Layer", MoveLayerEntry.ParameterTypes, MoveLayerEntry.DefaultValues),
            Primitive<ReplaceLayerEntry>("Layer", ReplaceLayerEntry.ParameterTypes),
            Primitive<AnimateEntry>("Animation", AnimateEntry.ParameterTypes, AnimateEntry.DefaultValues, AnimateEntry.ParameterOptions),
            Primitive<PlayAnimationPlanEntry>("Animation", PlayAnimationPlanEntry.ParameterTypes),
            Primitive<StopAnimationEntry>("Animation", StopAnimationEntry.ParameterTypes, StopAnimationEntry.DefaultValues, StopAnimationEntry.ParameterOptions),
            Primitive<PlayAudioEntry>("Audio", PlayAudioEntry.ParameterTypes, PlayAudioEntry.DefaultValues, PlayAudioEntry.ParameterOptions),
            Primitive<StopAudioEntry>("Audio", StopAudioEntry.ParameterTypes, StopAudioEntry.DefaultValues, StopAudioEntry.ParameterOptions),
            Primitive<PauseAudioEntry>("Audio", PauseAudioEntry.ParameterTypes, PauseAudioEntry.DefaultValues, PauseAudioEntry.ParameterOptions),
            Primitive<ResumeAudioEntry>("Audio", ResumeAudioEntry.ParameterTypes, ResumeAudioEntry.DefaultValues, ResumeAudioEntry.ParameterOptions),
            Primitive<EnqueueAudioEntry>("Audio", EnqueueAudioEntry.ParameterTypes, EnqueueAudioEntry.DefaultValues, EnqueueAudioEntry.ParameterOptions),
            Primitive<PlayVideoEntry>("Video", PlayVideoEntry.ParameterTypes),
            Primitive<StopVideoEntry>("Video", StopVideoEntry.ParameterTypes),
            Primitive<ApplyEffectEntry>("Effect", ApplyEffectEntry.ParameterTypes, ApplyEffectEntry.DefaultValues),
            Primitive<StopEffectEntry>("Effect", StopEffectEntry.ParameterTypes),
            Primitive<PlayParticleEmitterEntry>("Particle", PlayParticleEmitterEntry.ParameterTypes, PlayParticleEmitterEntry.DefaultValues),
            Primitive<StopParticleEmitterEntry>("Particle", StopParticleEmitterEntry.ParameterTypes),
            Primitive<WaitEntry>("Flow", WaitEntry.ParameterTypes, WaitEntry.DefaultValues),
            Primitive<SetVariableEntry>("Variable", SetVariableEntry.ParameterTypes),
            Primitive<UnlockGalleryEntry>("Gallery", UnlockGalleryEntry.ParameterTypes, options: UnlockGalleryEntry.ParameterOptions)
        };
        return primitives.Concat(EntryRegistry.Definitions).ToDictionary(definition => definition.Type, StringComparer.Ordinal);
    }

    private static EntryDefinition Primitive<TEntry>(
        string category,
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? options = null)
        where TEntry : PrimitiveEntry, new()
    {
        var typeId = new TEntry().Type;
        var resolvedDefaults = defaults ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptor = new PrimitiveDescriptor(
            typeId,
            parameters.Select(pair => new PrimitiveParameterDescriptor(
                pair.Key,
                ToPrimitiveKind(pair.Value),
                DefaultValue: resolvedDefaults.TryGetValue(pair.Key, out var value)
                    ? ToDefaultValue(value, pair.Value)
                    : null)).ToArray(),
            CreatesCheckpoint: typeId == TextEntry.TypeId);
        return new EntryDefinition(
            typeId,
            category,
            () => new AuthoringPrimitiveEntry(typeId),
            parameters,
            resolvedDefaults,
            options ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            EntryKind.Primitive,
            descriptor);
    }

    private static PrimitiveParameterKind ToPrimitiveKind(EntryParameterType type) => type switch
    {
        EntryParameterType.Integer => PrimitiveParameterKind.WholeNumber,
        EntryParameterType.Float => PrimitiveParameterKind.DecimalNumber,
        EntryParameterType.Json => PrimitiveParameterKind.JsonObject,
        _ => PrimitiveParameterKind.Text
    };

    private static JsonElement ToDefaultValue(string value, EntryParameterType type)
    {
        if (type == EntryParameterType.Json)
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        return type switch
        {
            EntryParameterType.Integer => JsonSerializer.SerializeToElement(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
            EntryParameterType.Float => JsonSerializer.SerializeToElement(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
            _ => JsonSerializer.SerializeToElement(value)
        };
    }

    private sealed class Catalog : IEntryCatalog
    {
        public IReadOnlyList<EntryDefinition> Definitions => BuiltinEntryCatalog.Definitions;
        public bool TryGet(string type, out EntryDefinition definition) => BuiltinEntryCatalog.TryGet(type, out definition);
        public EntryDefinition Get(string type) => BuiltinEntryCatalog.Get(type);
        public Entry Create(string type, int id = 0, string condition = "", IReadOnlyDictionary<string, string>? values = null) =>
            BuiltinEntryCatalog.Create(type, id, condition, values);
    }
}
