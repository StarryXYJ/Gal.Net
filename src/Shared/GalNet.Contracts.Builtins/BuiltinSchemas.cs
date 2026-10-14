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
        Reading<DialogueTextOptions>(TextEntry.TypeId),
        Schema<ShowDialogueEntry>(ShowDialogueEntry.ParameterTypes, null),
        Schema<HideDialogueEntry>(HideDialogueEntry.ParameterTypes, null)
    ]);

    public static IEntryModule CreateNvlModule() => new DefaultEntryModule("nvl",
    [
        Schema<ShowNvlEntry>(ShowNvlEntry.ParameterTypes, null),
        Schema<HideNvlEntry>(HideNvlEntry.ParameterTypes, null),
        Schema<ClearNvlEntry>(ClearNvlEntry.ParameterTypes, null),
        Reading<NvlAppendOptions>(AppendNvlEntry.TypeId)
    ]);

    public static IEntryModule CreateLayersModule() => new DefaultEntryModule("layers",
    [
        Schema<ShowLayerEntry>(ShowLayerEntry.ParameterTypes, ShowLayerEntry.DefaultValues, ShowLayerEntry.ParameterOptions),
        Schema<ShowColorLayerEntry>(ShowColorLayerEntry.ParameterTypes, ShowColorLayerEntry.DefaultValues),
        DefaultPrimitiveEntryBase.ForDto<LayerHandleOptions>(HideLayerEntry.TypeId),
        Schema<MoveLayerEntry>(MoveLayerEntry.ParameterTypes, MoveLayerEntry.DefaultValues),
        DefaultPrimitiveEntryBase.ForDto<LayerReplaceOptions>(ReplaceLayerEntry.TypeId)
    ]);

    public static IEntryModule CreateAnimationsModule() => new DefaultEntryModule("animations",
    [
        Schema<AnimateEntry>(AnimateEntry.ParameterTypes, AnimateEntry.DefaultValues, AnimateEntry.ParameterOptions),
        Schema<PlayAnimationPlanEntry>(PlayAnimationPlanEntry.ParameterTypes, null),
        DefaultPrimitiveEntryBase.ForDto<AnimationStopOptions>(StopAnimationEntry.TypeId)
    ]);

    public static IEntryModule CreateAudioModule() => new DefaultEntryModule("audio",
    [
        Schema<PlayAudioEntry>(PlayAudioEntry.ParameterTypes, PlayAudioEntry.DefaultValues, PlayAudioEntry.ParameterOptions),
        DefaultPrimitiveEntryBase.ForDto<AudioStopOptions>(StopAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(PauseAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(ResumeAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(NextAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(ClearAudioEntry.TypeId),
        Schema<EnqueueAudioEntry>(EnqueueAudioEntry.ParameterTypes, EnqueueAudioEntry.DefaultValues, EnqueueAudioEntry.ParameterOptions),
        Schema<ApplyAudioSEffectEntry>(ApplyAudioSEffectEntry.ParameterTypes, ApplyAudioSEffectEntry.DefaultValues),
        DefaultPrimitiveEntryBase.ForDto<AudioSEffectStopOptions>(StopAudioSEffectEntry.TypeId)
    ]);

    public static IEntryModule CreateVideoModule() => new DefaultEntryModule("video",
    [
        Schema<PlayVideoEntry>(PlayVideoEntry.ParameterTypes, PlayVideoEntry.DefaultValues, PlayVideoEntry.ParameterOptions),
        DefaultPrimitiveEntryBase.ForDto<VideoStopOptions>(StopVideoEntry.TypeId)
    ]);

    public static IEntryModule CreateEffectsModule() => new DefaultEntryModule("effects",
    [
        Schema<ApplyEffectEntry>(ApplyEffectEntry.ParameterTypes, ApplyEffectEntry.DefaultValues),
        DefaultPrimitiveEntryBase.ForDto<InstanceIdOptions>(StopEffectEntry.TypeId)
    ]);

    public static IEntryModule CreateParticlesModule() => new DefaultEntryModule("particles",
    [
        Schema<PlayParticleEmitterEntry>(PlayParticleEmitterEntry.ParameterTypes, PlayParticleEmitterEntry.DefaultValues),
        DefaultPrimitiveEntryBase.ForDto<InstanceIdOptions>(StopParticleEmitterEntry.TypeId),
        Schema<BurstParticlesEntry>(BurstParticlesEntry.ParameterTypes, BurstParticlesEntry.DefaultValues)
    ]);

    public static IEntryModule CreateFlowModule() => new DefaultEntryModule("flow",
    [
        DefaultPrimitiveEntryBase.ForDto<WaitOptions>(WaitEntry.TypeId)
    ]);

    public static IEntryModule CreateVariablesModule() => new DefaultEntryModule("variables",
    [
        DefaultPrimitiveEntryBase.ForDto<VariableSetOptions>(SetVariableEntry.TypeId)
    ]);

    public static IEntryModule CreateGalleryModule() => new DefaultEntryModule("gallery",
    [
        DefaultPrimitiveEntryBase.ForDto<GalleryUnlockOptions>(UnlockGalleryEntry.TypeId)
    ]);

    public static IEntryModule CreateTransitionsModule() =>
        new DefaultEntryModule("transitions", compositeEntries: BuiltinTransitionEntries.Definitions);

    public static IEntryModule CreateVoiceModule() => new DefaultEntryModule("voice", compositeEntries:
    [
        new DefaultCompositeEntryBase(TextVoiceEntry.TypeId, EntrySchema.DynamicParameters(TextVoiceEntry.ParameterTypes, TextVoiceEntry.DefaultValues), static () => new TextVoiceEntry()),
        new DefaultCompositeEntryBase(AppendVoiceNvlEntry.TypeId, EntrySchema.DynamicParameters(AppendVoiceNvlEntry.ParameterTypes, AppendVoiceNvlEntry.DefaultValues), static () => new AppendVoiceNvlEntry())
    ]);

    private static DefaultPrimitiveEntryBase Reading<T>(string type)
        where T : IFieldDto, IFieldSchema<T>, new() => new(type,
            new DynamicParameterTable(T.Schema.Select(field => new DynamicParameterDescriptor(field))))
        { FieldDataType = typeof(T), IsReading = true };

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
