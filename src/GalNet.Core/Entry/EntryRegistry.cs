namespace GalNet.Core.Entry;

public static class EntryRegistry
{
    private static readonly IReadOnlyDictionary<string, EntryDefinition> DefinitionsByType = BuildDefinitions();
    public static IReadOnlyList<EntryDefinition> Definitions { get; } = DefinitionsByType.Values.ToArray();

    public static bool TryGet(string type, out EntryDefinition definition) => DefinitionsByType.TryGetValue(type, out definition!);

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
        if (values is not null)
        {
            if (type == SetVariableEntry.TypeId && values.Keys.Any(name => !definition.Parameters.ContainsKey(name)))
                throw new InvalidDataException("The variable.set entry only accepts 'target' and 'expression'.");
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
            Define(TextEntry.TypeId, "Dialogue", () => new TextEntry(), TextEntry.ParameterTypes, TextEntry.DefaultValues),
            Define(ShowDialogueEntry.TypeId, "Dialogue", () => new ShowDialogueEntry(), ShowDialogueEntry.ParameterTypes),
            Define(HideDialogueEntry.TypeId, "Dialogue", () => new HideDialogueEntry(), HideDialogueEntry.ParameterTypes),
            Define(ShowLayerEntry.TypeId, "Layer", () => new ShowLayerEntry(), ShowLayerEntry.ParameterTypes, ShowLayerEntry.DefaultValues, ShowLayerEntry.ParameterOptions),
            Define(HideLayerEntry.TypeId, "Layer", () => new HideLayerEntry(), HideLayerEntry.ParameterTypes, HideLayerEntry.DefaultValues, HideLayerEntry.ParameterOptions),
            Define(MoveLayerEntry.TypeId, "Layer", () => new MoveLayerEntry(), MoveLayerEntry.ParameterTypes, MoveLayerEntry.DefaultValues),
            Define(PlayAudioEntry.TypeId, "Audio", () => new PlayAudioEntry(), PlayAudioEntry.ParameterTypes, PlayAudioEntry.DefaultValues, PlayAudioEntry.ParameterOptions),
            Define(StopAudioEntry.TypeId, "Audio", () => new StopAudioEntry(), StopAudioEntry.ParameterTypes, StopAudioEntry.DefaultValues, StopAudioEntry.ParameterOptions),
            Define(PauseAudioEntry.TypeId, "Audio", () => new PauseAudioEntry(), PauseAudioEntry.ParameterTypes, PauseAudioEntry.DefaultValues, PauseAudioEntry.ParameterOptions),
            Define(ResumeAudioEntry.TypeId, "Audio", () => new ResumeAudioEntry(), ResumeAudioEntry.ParameterTypes, ResumeAudioEntry.DefaultValues, ResumeAudioEntry.ParameterOptions),
            Define(EnqueueAudioEntry.TypeId, "Audio", () => new EnqueueAudioEntry(), EnqueueAudioEntry.ParameterTypes, EnqueueAudioEntry.DefaultValues, EnqueueAudioEntry.ParameterOptions),
            Define(PlayVideoEntry.TypeId, "Video", () => new PlayVideoEntry(), PlayVideoEntry.ParameterTypes),
            Define(StopVideoEntry.TypeId, "Video", () => new StopVideoEntry(), StopVideoEntry.ParameterTypes),
            Define(ApplyEffectEntry.TypeId, "Effect", () => new ApplyEffectEntry(), ApplyEffectEntry.ParameterTypes),
            Define(StopEffectEntry.TypeId, "Effect", () => new StopEffectEntry(), StopEffectEntry.ParameterTypes),
            Define(WaitEntry.TypeId, "Flow", () => new WaitEntry(), WaitEntry.ParameterTypes, WaitEntry.DefaultValues),
            Define(SetVariableEntry.TypeId, "Variable", () => new SetVariableEntry(), SetVariableEntry.ParameterTypes),
            Define(UnlockGalleryEntry.TypeId, "Gallery", () => new UnlockGalleryEntry(), UnlockGalleryEntry.ParameterTypes, options: UnlockGalleryEntry.ParameterOptions)
        };
        return new Dictionary<string, EntryDefinition>(definitions.ToDictionary(x => x.Type), StringComparer.Ordinal);
    }

    private static EntryDefinition Define(
        string type,
        string category,
        Func<Entry> factory,
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? options = null) =>
        new(type, category, factory, parameters, defaults ?? new Dictionary<string, string>(), options ?? new Dictionary<string, IReadOnlyList<string>>());
}
