using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GalNet.Contracts.Builtins;

/// <summary>Independent built-in schema modules; no execution service is created.</summary>
public static class BuiltinSchemas
{
    public static IReadOnlyList<IEntryModule> CreateAllModules() => Array.AsReadOnly<IEntryModule>(
    [
        CreateDialogueModule(),
        CreateNvlModule(),
        CreateLayersModule(),
        CreateAnimationsModule(),
        CreateAudioModule(),
        CreateVideoModule(),
        CreateEffectsModule(),
        CreateParticlesModule(),
        CreateFlowModule(),
        CreateVariablesModule(),
        CreateGalleryModule(),
        CreateTransitionsModule(),
        CreateVoiceModule()
    ]);

    public static IEntryModule CreateDialogueModule() => new DefaultEntryModule("dialogue",
    [
        Schema<TextEntry>(TextEntry.ParameterTypes, TextEntry.DefaultValues),
        Schema<ShowDialogueEntry>(ShowDialogueEntry.ParameterTypes, null),
        Schema<HideDialogueEntry>(HideDialogueEntry.ParameterTypes, null)
    ]);

    public static IEntryModule CreateNvlModule() => new DefaultEntryModule("nvl",
    [
        Schema<ShowNvlEntry>(ShowNvlEntry.ParameterTypes, null),
        Schema<HideNvlEntry>(HideNvlEntry.ParameterTypes, null),
        Schema<ClearNvlEntry>(ClearNvlEntry.ParameterTypes, null),
        Schema<AppendNvlEntry>(AppendNvlEntry.ParameterTypes, null)
    ]);

    public static IEntryModule CreateLayersModule() => new DefaultEntryModule("layers",
    [
        Schema<ShowLayerEntry>(ShowLayerEntry.ParameterTypes, ShowLayerEntry.DefaultValues, ShowLayerEntry.ParameterOptions),
        Schema<ShowColorLayerEntry>(ShowColorLayerEntry.ParameterTypes, ShowColorLayerEntry.DefaultValues),
        Schema<HideLayerEntry>(HideLayerEntry.ParameterTypes, null),
        Schema<MoveLayerEntry>(MoveLayerEntry.ParameterTypes, MoveLayerEntry.DefaultValues),
        Schema<ReplaceLayerEntry>(ReplaceLayerEntry.ParameterTypes, null)
    ]);

    public static IEntryModule CreateAnimationsModule() => new DefaultEntryModule("animations",
    [
        Schema<AnimateEntry>(AnimateEntry.ParameterTypes, AnimateEntry.DefaultValues, AnimateEntry.ParameterOptions),
        Schema<PlayAnimationPlanEntry>(PlayAnimationPlanEntry.ParameterTypes, null),
        Schema<StopAnimationEntry>(StopAnimationEntry.ParameterTypes, StopAnimationEntry.DefaultValues, StopAnimationEntry.ParameterOptions)
    ]);

    public static IEntryModule CreateAudioModule() => new DefaultEntryModule("audio",
    [
        Schema<PlayAudioEntry>(PlayAudioEntry.ParameterTypes, PlayAudioEntry.DefaultValues, PlayAudioEntry.ParameterOptions),
        Schema<StopAudioEntry>(StopAudioEntry.ParameterTypes, StopAudioEntry.DefaultValues, StopAudioEntry.ParameterOptions),
        Schema<PauseAudioEntry>(PauseAudioEntry.ParameterTypes, PauseAudioEntry.DefaultValues),
        Schema<ResumeAudioEntry>(ResumeAudioEntry.ParameterTypes, ResumeAudioEntry.DefaultValues),
        Schema<NextAudioEntry>(NextAudioEntry.ParameterTypes, NextAudioEntry.DefaultValues),
        Schema<ClearAudioEntry>(ClearAudioEntry.ParameterTypes, ClearAudioEntry.DefaultValues),
        Schema<EnqueueAudioEntry>(EnqueueAudioEntry.ParameterTypes, EnqueueAudioEntry.DefaultValues, EnqueueAudioEntry.ParameterOptions),
        Schema<ApplyAudioSEffectEntry>(ApplyAudioSEffectEntry.ParameterTypes, ApplyAudioSEffectEntry.DefaultValues),
        Schema<StopAudioSEffectEntry>(StopAudioSEffectEntry.ParameterTypes, StopAudioSEffectEntry.DefaultValues)
    ]);

    public static IEntryModule CreateVideoModule() => new DefaultEntryModule("video",
    [
        Schema<PlayVideoEntry>(PlayVideoEntry.ParameterTypes, PlayVideoEntry.DefaultValues, PlayVideoEntry.ParameterOptions),
        Schema<StopVideoEntry>(StopVideoEntry.ParameterTypes, StopVideoEntry.DefaultValues)
    ]);

    public static IEntryModule CreateEffectsModule() => new DefaultEntryModule("effects",
    [
        Schema<ApplyEffectEntry>(ApplyEffectEntry.ParameterTypes, ApplyEffectEntry.DefaultValues),
        Schema<StopEffectEntry>(StopEffectEntry.ParameterTypes, null)
    ]);

    public static IEntryModule CreateParticlesModule() => new DefaultEntryModule("particles",
    [
        Schema<PlayParticleEmitterEntry>(PlayParticleEmitterEntry.ParameterTypes, PlayParticleEmitterEntry.DefaultValues),
        Schema<StopParticleEmitterEntry>(StopParticleEmitterEntry.ParameterTypes, null),
        Schema<BurstParticlesEntry>(BurstParticlesEntry.ParameterTypes, BurstParticlesEntry.DefaultValues)
    ]);

    public static IEntryModule CreateFlowModule() => new DefaultEntryModule("flow",
    [
        Schema<WaitEntry>(WaitEntry.ParameterTypes, WaitEntry.DefaultValues)
    ]);

    public static IEntryModule CreateVariablesModule() => new DefaultEntryModule("variables",
    [
        Schema<SetVariableEntry>(SetVariableEntry.ParameterTypes, null)
    ]);

    public static IEntryModule CreateGalleryModule() => new DefaultEntryModule("gallery",
    [
        Schema<UnlockGalleryEntry>(UnlockGalleryEntry.ParameterTypes, null)
    ]);

    public static IEntryModule CreateTransitionsModule() =>
        new DefaultEntryModule("transitions", compositeEntries: BuiltinTransitionEntries.Definitions);

    public static IEntryModule CreateVoiceModule() => new DefaultEntryModule("voice", compositeEntries:
    [
        new DefaultCompositeEntryBase(TextVoiceEntry.TypeId, EntrySchema.DynamicParameters(TextVoiceEntry.ParameterTypes, TextVoiceEntry.DefaultValues), static () => new TextVoiceEntry()),
        new DefaultCompositeEntryBase(AppendVoiceNvlEntry.TypeId, EntrySchema.DynamicParameters(AppendVoiceNvlEntry.ParameterTypes, AppendVoiceNvlEntry.DefaultValues), static () => new AppendVoiceNvlEntry())
    ]);

    private static DefaultPrimitiveEntryBase Schema<TEntry>(
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? options = null)
        where TEntry : PrimitiveEntry, new()
    {
        var type = new TEntry().Type;
        var declared = EntrySchema.DynamicParameters(parameters, defaults, options);
        return new DefaultPrimitiveEntryBase(type, new DynamicParameterTable(declared.Values.Select(parameter =>
        {
            int? minimum = parameter.Name is "duration" or "fadeIn" or "fadeOut" ? 0
                : type == BurstParticlesEntry.TypeId && parameter.Name == "count" ? 1 : null;
            if (minimum is null) return parameter;
            var constraints = parameter.Constraints?.Deserialize<Dictionary<string, JsonElement>>() ?? new();
            constraints["minimum"] = JsonSerializer.SerializeToElement(minimum.Value);
            return new DynamicParameterDescriptor(parameter.Name,
                new ValueDescriptor(parameter.ValueType, parameter.AllowNull, JsonSerializer.SerializeToElement(constraints)),
                parameter.IsRequired, parameter.DefaultValue, parameter.Editor);
        }), arguments => BuiltinArgumentValidation.Validate(type, arguments)))
        { IsReading = type is TextEntry.TypeId or AppendNvlEntry.TypeId };
    }
}
