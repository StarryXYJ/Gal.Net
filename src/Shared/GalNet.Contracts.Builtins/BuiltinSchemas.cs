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
        Reading<DialogueTextOptions>(BuiltinEntryIds.Text),
        new DefaultPrimitiveEntryBase(BuiltinEntryIds.ShowDialogue, DynamicParameterTable.Empty),
        new DefaultPrimitiveEntryBase(BuiltinEntryIds.HideDialogue, DynamicParameterTable.Empty)
    ]);

    public static IEntryModule CreateNvlModule() => new DefaultEntryModule("nvl",
    [
        new DefaultPrimitiveEntryBase(BuiltinEntryIds.ShowNvl, DynamicParameterTable.Empty),
        new DefaultPrimitiveEntryBase(BuiltinEntryIds.HideNvl, DynamicParameterTable.Empty),
        new DefaultPrimitiveEntryBase(BuiltinEntryIds.ClearNvl, DynamicParameterTable.Empty),
        Reading<NvlAppendOptions>(BuiltinEntryIds.AppendNvl)
    ]);

    public static IEntryModule CreateLayersModule() => new DefaultEntryModule("layers",
    [
        DefaultPrimitiveEntryBase.ForDto<LayerShowOptions>(BuiltinEntryIds.ShowLayer),
        DefaultPrimitiveEntryBase.ForDto<LayerColorOptions>(BuiltinEntryIds.ShowColorLayer),
        DefaultPrimitiveEntryBase.ForDto<LayerHandleOptions>(BuiltinEntryIds.HideLayer),
        DefaultPrimitiveEntryBase.ForDto<LayerMoveOptions>(BuiltinEntryIds.MoveLayer),
        DefaultPrimitiveEntryBase.ForDto<LayerReplaceOptions>(BuiltinEntryIds.ReplaceLayer)
    ]);

    public static IEntryModule CreateAnimationsModule() => new DefaultEntryModule("animations",
    [
        DefaultPrimitiveEntryBase.ForDto<AnimationOptions>(BuiltinEntryIds.Animate),
        DefaultPrimitiveEntryBase.ForDto<AnimationPlanOptions>(BuiltinEntryIds.PlayAnimationPlan, arguments => BuiltinArgumentValidation.Validate(BuiltinEntryIds.PlayAnimationPlan, arguments), validatePartialArguments: static _ => { }),
        DefaultPrimitiveEntryBase.ForDto<AnimationStopOptions>(BuiltinEntryIds.StopAnimation)
    ]);

    public static IEntryModule CreateAudioModule() => new DefaultEntryModule("audio",
    [
        DefaultPrimitiveEntryBase.ForDto<AudioPlayOptions>(BuiltinEntryIds.PlayAudio),
        DefaultPrimitiveEntryBase.ForDto<AudioStopOptions>(BuiltinEntryIds.StopAudio),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(BuiltinEntryIds.PauseAudio),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(BuiltinEntryIds.ResumeAudio),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(BuiltinEntryIds.NextAudio),
        DefaultPrimitiveEntryBase.ForDto<AudioTrackOptions>(BuiltinEntryIds.ClearAudio),
        DefaultPrimitiveEntryBase.ForDto<AudioPlayOptions>(BuiltinEntryIds.EnqueueAudio),
        DefaultPrimitiveEntryBase.ForDto<AudioSEffectOptions>(BuiltinEntryIds.ApplyAudioSEffect),
        DefaultPrimitiveEntryBase.ForDto<AudioSEffectStopOptions>(BuiltinEntryIds.StopAudioSEffect)
    ]);

    public static IEntryModule CreateVideoModule() => new DefaultEntryModule("video",
    [
        DefaultPrimitiveEntryBase.ForDto<VideoPlayOptions>(BuiltinEntryIds.PlayVideo, arguments => BuiltinArgumentValidation.Validate(BuiltinEntryIds.PlayVideo, arguments), validatePartialArguments: static _ => { }),
        DefaultPrimitiveEntryBase.ForDto<VideoStopOptions>(BuiltinEntryIds.StopVideo)
    ]);

    public static IEntryModule CreateEffectsModule() => new DefaultEntryModule("effects",
    [
        DefaultPrimitiveEntryBase.ForDto<EffectApplyOptions>(BuiltinEntryIds.ApplyEffect),
        DefaultPrimitiveEntryBase.ForDto<InstanceIdOptions>(BuiltinEntryIds.StopEffect)
    ]);

    public static IEntryModule CreateParticlesModule() => new DefaultEntryModule("particles",
    [
        DefaultPrimitiveEntryBase.ForDto<ParticlePlayOptions>(BuiltinEntryIds.PlayParticleEmitter, arguments => BuiltinArgumentValidation.Validate(BuiltinEntryIds.PlayParticleEmitter, arguments), validatePartialArguments: static _ => { }),
        DefaultPrimitiveEntryBase.ForDto<InstanceIdOptions>(BuiltinEntryIds.StopParticleEmitter),
        DefaultPrimitiveEntryBase.ForDto<ParticleBurstOptions>(BuiltinEntryIds.BurstParticles, arguments => BuiltinArgumentValidation.Validate(BuiltinEntryIds.BurstParticles, arguments), validatePartialArguments: static _ => { })
    ]);

    public static IEntryModule CreateFlowModule() => new DefaultEntryModule("flow",
    [
        DefaultPrimitiveEntryBase.ForDto<WaitOptions>(BuiltinEntryIds.Wait)
    ]);

    public static IEntryModule CreateVariablesModule() => new DefaultEntryModule("variables",
    [
        DefaultPrimitiveEntryBase.ForDto<VariableSetOptions>(BuiltinEntryIds.SetVariable)
    ]);

    public static IEntryModule CreateGalleryModule() => new DefaultEntryModule("gallery",
    [
        DefaultPrimitiveEntryBase.ForDto<GalleryUnlockOptions>(BuiltinEntryIds.UnlockGallery)
    ]);

    public static IEntryModule CreateTransitionsModule() =>
        new DefaultEntryModule("transitions", compositeEntries: BuiltinTransitionEntries.Definitions);

    public static IEntryModule CreateVoiceModule() => new DefaultEntryModule("voice", compositeEntries:
    [
        DefaultCompositeEntryBase.ForDto<TextVoiceOptions>(BuiltinEntryIds.TextVoice, VoiceTextEntrySupport.Text),
        DefaultCompositeEntryBase.ForDto<NvlVoiceOptions>(BuiltinEntryIds.AppendVoiceNvl, VoiceTextEntrySupport.Nvl)
    ]);

    private static DefaultPrimitiveEntryBase Reading<T>(string type)
        where T : IFieldDto, IFieldSchema<T>, new() => new(type,
            new DynamicParameterTable(T.Schema.Select(field => new DynamicParameterDescriptor(field))))
        { FieldDataType = typeof(T), IsReading = true };

}
