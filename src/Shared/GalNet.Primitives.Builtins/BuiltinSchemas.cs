using GalNet.Core.Entry;

namespace GalNet.Primitives.Builtins;

/// <summary>Built-in contracts and compile-time expansions; no runtime service is created.</summary>
public static class BuiltinSchemas
{
    public static IEntryModule CreateModule() => new DefaultEntryModule("builtins",
        [
            Schema<TextEntry>(TextEntry.ParameterTypes, TextEntry.DefaultValues),
            Schema<ShowDialogueEntry>(ShowDialogueEntry.ParameterTypes, null),
            Schema<HideDialogueEntry>(HideDialogueEntry.ParameterTypes, null),
            Schema<ApplyEffectEntry>(ApplyEffectEntry.ParameterTypes, ApplyEffectEntry.DefaultValues),
            Schema<StopEffectEntry>(StopEffectEntry.ParameterTypes, null),
            Schema<WaitEntry>(WaitEntry.ParameterTypes, WaitEntry.DefaultValues),
            Schema<SetVariableEntry>(SetVariableEntry.ParameterTypes, null),
            Schema<UnlockGalleryEntry>(UnlockGalleryEntry.ParameterTypes, null),
            Schema<ShowLayerEntry>(ShowLayerEntry.ParameterTypes, ShowLayerEntry.DefaultValues, ShowLayerEntry.ParameterOptions),
            Schema<ShowColorLayerEntry>(ShowColorLayerEntry.ParameterTypes, ShowColorLayerEntry.DefaultValues),
            Schema<HideLayerEntry>(HideLayerEntry.ParameterTypes, null),
            Schema<MoveLayerEntry>(MoveLayerEntry.ParameterTypes, MoveLayerEntry.DefaultValues),
            Schema<ReplaceLayerEntry>(ReplaceLayerEntry.ParameterTypes, null),
            Schema<AnimateEntry>(AnimateEntry.ParameterTypes, AnimateEntry.DefaultValues, AnimateEntry.ParameterOptions),
            Schema<PlayAnimationPlanEntry>(PlayAnimationPlanEntry.ParameterTypes, null),
            Schema<StopAnimationEntry>(StopAnimationEntry.ParameterTypes, StopAnimationEntry.DefaultValues, StopAnimationEntry.ParameterOptions),
            Schema<PlayAudioEntry>(PlayAudioEntry.ParameterTypes, PlayAudioEntry.DefaultValues, PlayAudioEntry.ParameterOptions),
            Schema<StopAudioEntry>(StopAudioEntry.ParameterTypes, StopAudioEntry.DefaultValues, StopAudioEntry.ParameterOptions),
            Schema<PauseAudioEntry>(PauseAudioEntry.ParameterTypes, PauseAudioEntry.DefaultValues),
            Schema<ResumeAudioEntry>(ResumeAudioEntry.ParameterTypes, ResumeAudioEntry.DefaultValues),
            Schema<NextAudioEntry>(NextAudioEntry.ParameterTypes, NextAudioEntry.DefaultValues),
            Schema<ClearAudioEntry>(ClearAudioEntry.ParameterTypes, ClearAudioEntry.DefaultValues),
            Schema<EnqueueAudioEntry>(EnqueueAudioEntry.ParameterTypes, EnqueueAudioEntry.DefaultValues, EnqueueAudioEntry.ParameterOptions),
            Schema<ApplyAudioSEffectEntry>(ApplyAudioSEffectEntry.ParameterTypes, ApplyAudioSEffectEntry.DefaultValues),
            Schema<StopAudioSEffectEntry>(StopAudioSEffectEntry.ParameterTypes, StopAudioSEffectEntry.DefaultValues),
            Schema<PlayVideoEntry>(PlayVideoEntry.ParameterTypes, PlayVideoEntry.DefaultValues, PlayVideoEntry.ParameterOptions),
            Schema<StopVideoEntry>(StopVideoEntry.ParameterTypes, StopVideoEntry.DefaultValues),
            Schema<ShowNvlEntry>(ShowNvlEntry.ParameterTypes, null),
            Schema<HideNvlEntry>(HideNvlEntry.ParameterTypes, null),
            Schema<ClearNvlEntry>(ClearNvlEntry.ParameterTypes, null),
            Schema<AppendNvlEntry>(AppendNvlEntry.ParameterTypes, null),
            Schema<PlayParticleEmitterEntry>(PlayParticleEmitterEntry.ParameterTypes, PlayParticleEmitterEntry.DefaultValues),
            Schema<StopParticleEmitterEntry>(StopParticleEmitterEntry.ParameterTypes, null),
            Schema<BurstParticlesEntry>(BurstParticlesEntry.ParameterTypes, BurstParticlesEntry.DefaultValues)
        ],
        BuiltinTransitionEntries.Definitions.Concat(
        [
            new DefaultCompositeEntryBase(TextVoiceEntry.TypeId, EntrySchema.DynamicParameters(TextVoiceEntry.ParameterTypes, TextVoiceEntry.DefaultValues), static () => new TextVoiceEntry()),
            new DefaultCompositeEntryBase(AppendVoiceNvlEntry.TypeId, EntrySchema.DynamicParameters(AppendVoiceNvlEntry.ParameterTypes, AppendVoiceNvlEntry.DefaultValues), static () => new AppendVoiceNvlEntry())
        ]));

    public static TargetProfileEntryCatalog CreateCatalog() => new([CreateModule()]);

    private static DefaultPrimitiveEntryBase Schema<TEntry>(
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? options = null)
        where TEntry : PrimitiveEntry, new()
    {
        var type = new TEntry().Type;
        return new DefaultPrimitiveEntryBase(type, EntrySchema.DynamicParameters(parameters, defaults, options,
            arguments => BuiltinArgumentValidation.Validate(type, arguments)))
        { IsReading = type is TextEntry.TypeId or AppendNvlEntry.TypeId };
    }
}
