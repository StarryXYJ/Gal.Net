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
        DefaultPrimitiveEntryBase.ForDto<LayerShowOptions>(ShowLayerEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<LayerColorOptions>(ShowColorLayerEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<LayerHandleOptions>(HideLayerEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<LayerMoveOptions>(MoveLayerEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<LayerReplaceOptions>(ReplaceLayerEntry.TypeId)
    ]);

    public static IEntryModule CreateAnimationsModule() => new DefaultEntryModule("animations",
    [
        DefaultPrimitiveEntryBase.ForDto<AnimationOptions>(AnimateEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AnimationPlanOptions>(PlayAnimationPlanEntry.TypeId, arguments => BuiltinArgumentValidation.Validate(PlayAnimationPlanEntry.TypeId, arguments), validatePartialArguments: static _ => { }),
        DefaultPrimitiveEntryBase.ForDto<AnimationStopOptions>(StopAnimationEntry.TypeId)
    ]);

    public static IEntryModule CreateAudioModule() => new DefaultEntryModule("audio",
    [
        DefaultPrimitiveEntryBase.ForDto<AudioPlayOptions>(PlayAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioStopOptions>(StopAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(PauseAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(ResumeAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(NextAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(ClearAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioPlayOptions>(EnqueueAudioEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioSEffectOptions>(ApplyAudioSEffectEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<AudioSEffectStopOptions>(StopAudioSEffectEntry.TypeId)
    ]);

    public static IEntryModule CreateVideoModule() => new DefaultEntryModule("video",
    [
        DefaultPrimitiveEntryBase.ForDto<VideoPlayOptions>(PlayVideoEntry.TypeId, arguments => BuiltinArgumentValidation.Validate(PlayVideoEntry.TypeId, arguments), validatePartialArguments: static _ => { }),
        DefaultPrimitiveEntryBase.ForDto<VideoStopOptions>(StopVideoEntry.TypeId)
    ]);

    public static IEntryModule CreateEffectsModule() => new DefaultEntryModule("effects",
    [
        DefaultPrimitiveEntryBase.ForDto<EffectApplyOptions>(ApplyEffectEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<InstanceIdOptions>(StopEffectEntry.TypeId)
    ]);

    public static IEntryModule CreateParticlesModule() => new DefaultEntryModule("particles",
    [
        DefaultPrimitiveEntryBase.ForDto<ParticlePlayOptions>(PlayParticleEmitterEntry.TypeId, arguments => BuiltinArgumentValidation.Validate(PlayParticleEmitterEntry.TypeId, arguments), validatePartialArguments: static _ => { }),
        DefaultPrimitiveEntryBase.ForDto<InstanceIdOptions>(StopParticleEmitterEntry.TypeId),
        DefaultPrimitiveEntryBase.ForDto<ParticleBurstOptions>(BurstParticlesEntry.TypeId, arguments => BuiltinArgumentValidation.Validate(BurstParticlesEntry.TypeId, arguments), validatePartialArguments: static _ => { })
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
