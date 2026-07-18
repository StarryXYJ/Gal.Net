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
            foreach (var (name, value) in values)
                if (definition.Parameters.ContainsKey(name))
                    entry.Values[name] = value;
        return entry;
    }

    private static IReadOnlyDictionary<string, EntryDefinition> BuildDefinitions()
    {
        var definitions = new[]
        {
            Define(TextEntry.TypeId, () => new TextEntry(), TextEntry.ParameterTypes, TextEntry.DefaultValues),
            Define(ShowLayerEntry.TypeId, () => new ShowLayerEntry(), ShowLayerEntry.ParameterTypes, ShowLayerEntry.DefaultValues, ShowLayerEntry.ParameterOptions),
            Define(HideLayerEntry.TypeId, () => new HideLayerEntry(), HideLayerEntry.ParameterTypes, HideLayerEntry.DefaultValues, HideLayerEntry.ParameterOptions),
            Define(MoveLayerEntry.TypeId, () => new MoveLayerEntry(), MoveLayerEntry.ParameterTypes, MoveLayerEntry.DefaultValues),
            Define(PlayAudioEntry.TypeId, () => new PlayAudioEntry(), PlayAudioEntry.ParameterTypes, PlayAudioEntry.DefaultValues, PlayAudioEntry.ParameterOptions),
            Define(StopAudioEntry.TypeId, () => new StopAudioEntry(), StopAudioEntry.ParameterTypes, StopAudioEntry.DefaultValues, StopAudioEntry.ParameterOptions),
            Define(PauseAudioEntry.TypeId, () => new PauseAudioEntry(), PauseAudioEntry.ParameterTypes, PauseAudioEntry.DefaultValues, PauseAudioEntry.ParameterOptions),
            Define(ResumeAudioEntry.TypeId, () => new ResumeAudioEntry(), ResumeAudioEntry.ParameterTypes, ResumeAudioEntry.DefaultValues, ResumeAudioEntry.ParameterOptions),
            Define(EnqueueAudioEntry.TypeId, () => new EnqueueAudioEntry(), EnqueueAudioEntry.ParameterTypes, EnqueueAudioEntry.DefaultValues, EnqueueAudioEntry.ParameterOptions),
            Define(PlayVideoEntry.TypeId, () => new PlayVideoEntry(), PlayVideoEntry.ParameterTypes),
            Define(StopVideoEntry.TypeId, () => new StopVideoEntry(), StopVideoEntry.ParameterTypes),
            Define(ShowControlEntry.TypeId, () => new ShowControlEntry(), ShowControlEntry.ParameterTypes),
            Define(HideControlEntry.TypeId, () => new HideControlEntry(), HideControlEntry.ParameterTypes),
            Define(SetControlEntry.TypeId, () => new SetControlEntry(), SetControlEntry.ParameterTypes),
            Define(ApplyEffectEntry.TypeId, () => new ApplyEffectEntry(), ApplyEffectEntry.ParameterTypes),
            Define(StopEffectEntry.TypeId, () => new StopEffectEntry(), StopEffectEntry.ParameterTypes),
            Define(WaitEntry.TypeId, () => new WaitEntry(), WaitEntry.ParameterTypes, WaitEntry.DefaultValues),
            Define(SetVariableEntry.TypeId, () => new SetVariableEntry(), SetVariableEntry.ParameterTypes, SetVariableEntry.DefaultValues, SetVariableEntry.ParameterOptions),
            Define(EvaluateVariableEntry.TypeId, () => new EvaluateVariableEntry(), EvaluateVariableEntry.ParameterTypes),
            Define(UnlockGalleryEntry.TypeId, () => new UnlockGalleryEntry(), UnlockGalleryEntry.ParameterTypes, options: UnlockGalleryEntry.ParameterOptions)
        };
        return new Dictionary<string, EntryDefinition>(definitions.ToDictionary(x => x.Type), StringComparer.Ordinal);
    }

    private static EntryDefinition Define(
        string type,
        Func<Entry> factory,
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? options = null) =>
        new(type, factory, parameters, defaults ?? new Dictionary<string, string>(), options ?? new Dictionary<string, IReadOnlyList<string>>());
}
