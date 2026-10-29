using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GalNet.Core.Parameters;
using GalNet.Core.Runtime;
using GalNet.Core.Serialization;

[assembly: FieldSchemaJsonContext(typeof(GalNet.Contracts.Builtins.BuiltinJsonContext))]

namespace GalNet.Contracts.Builtins;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, UseStringEnumConverter = true)]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.AnimationOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.AnimationStopOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Assets.SpriteAssetMeta))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Audio.AudioChannelState[]))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Audio.AudioSEffectState[]))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Audio.AudioTrackState[]))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.AudioLevelOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.AudioPlayOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.AudioSEffectOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.AudioSEffectStopOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.AudioSEffectUpdateOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.AudioStopOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.AudioTrackOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.BlindsOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.BlurTransitionOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.ColorFadeOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.ColorFieldOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.CrossFadeOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.CustomInvokeOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.DefaultProjectOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.DialogueTextOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.EffectApplyOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Gallery.GalleryAnnotation[]))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Gallery.GalleryConfiguration))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.GalleryUnlockOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.ImpactPresetOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.InstanceIdOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.LayerColorOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.LayerHandleOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.LayerMoveOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.LayerReplaceOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.LayerShowOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.MotionPresetOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.NvlAppendOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.NvlVoiceOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.ParticleBurstOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.ParticlePlayOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.RevealTransitionOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Scene.EffectInstance[]))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Scene.Layer[]))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Scene.LoopAnimationState[]))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Scene.ParticleEmitterInstance[]))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Scene.TimelineDefinition))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.ShakePresetOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.SlideOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.SpeedLinesPresetOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.TextVoiceOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.TimelineOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.VariableSetOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.Variants.ImageVariantsSource))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.VariantsSelectOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.VariantsShowOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.VideoPlayOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.VideoStopOptions))]
[JsonSerializable(typeof(global::GalNet.Contracts.Builtins.WaitOptions))]
[JsonSerializable(typeof(global::System.Collections.Generic.Dictionary<string, string>))]
[JsonSerializable(typeof(global::System.Collections.Generic.List<GalNet.Core.I18n.I18nLocale>))]
internal sealed partial class BuiltinJsonContext : JsonSerializerContext;

public static class BuiltinJson
{
    private static readonly JsonSerializerOptions StateOptions = CreateOptions(GameState.JsonOptions);
    public static IJsonTypeInfoResolver Resolver => BuiltinJsonContext.Default;
    public static JsonSerializerOptions CreateOptions(JsonSerializerOptions? template = null, bool stringEnums = false) =>
        JsonMetadata.CreateOptions(template, Resolver, stringEnums);
    public static JsonTypeInfo<T[]> StateTypeInfo<T>() => JsonMetadata.GetTypeInfo<T[]>(StateOptions);
}
