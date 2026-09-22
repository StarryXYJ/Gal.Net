using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Core.View;

namespace GalNet.Primitives.Builtins;

/// <summary>Recommended schemas plus the small runtime modules implemented by this feature.</summary>
public static class BuiltinEntryModules
{
    public static IReadOnlyList<IEntryModule> CreateRecommended(
        IDialoguePresenter? dialoguePresenter = null,
        ILayerPresenter? layerPresenter = null,
        IAnimationPresenter? animationPresenter = null,
        IEffectPresenter? effectPresenter = null) => Array.AsReadOnly<IEntryModule>(
    [
        new BuiltinDialogueModule(dialoguePresenter),
        new BuiltinLayerModule(layerPresenter),
        new BuiltinAnimationModule(animationPresenter, layerPresenter, effectPresenter),
        new BuiltinAudioModule(),
        new BuiltinVideoModule(),
        new BuiltinEffectModule(effectPresenter),
        new BuiltinParticleModule(),
        new BuiltinFlowModule(),
        new BuiltinVariableModule(),
        new BuiltinGalleryModule()
    ]);

    public static TargetProfileEntryCatalog CreateRecommendedTargetProfile() => new(CreateRecommended());
}

public sealed class BuiltinDialogueModule : EntryModuleBase
{
    public BuiltinDialogueModule(IDialoguePresenter? presenter = null) : base("dialogue",
    [
        Text(presenter),
        Visibility<ShowDialogueEntry>(presenter, true),
        Visibility<HideDialogueEntry>(presenter, false)
    ]) { }

    private static PrimitiveEntryBase Text(IDialoguePresenter? presenter) => new DefaultPrimitiveEntryBase(
        TextEntry.TypeId,
        EntrySchema.DynamicParameters(TextEntry.ParameterTypes, TextEntry.DefaultValues),
        context => new DialoguePrimitiveInstance(
            presenter ?? throw new InvalidOperationException("The dialogue module has no presenter."),
            Arguments.String(context, "speaker"),
            Arguments.String(context, "content"),
            Arguments.String(context, "voice"),
            context.BatchId,
            context.ScopeCancellation));

    private static PrimitiveEntryBase Visibility<TEntry>(IDialoguePresenter? presenter, bool visible)
        where TEntry : PrimitiveEntry, new() => new DefaultPrimitiveEntryBase(
        new TEntry().Type,
        DynamicParameterTable.Empty,
        context => new ImmediatePrimitiveInstance(() =>
        {
            var target = presenter ?? throw new InvalidOperationException("The dialogue module has no presenter.");
            if (visible) target.ShowDialogue(); else target.HideDialogue();
        }, batchId: context.BatchId));
}

public sealed class BuiltinLayerModule : EntryModuleBase
{
    public BuiltinLayerModule(ILayerPresenter? presenter = null) : base("layer",
    [
        Primitive<ShowLayerEntry>(ShowLayerEntry.ParameterTypes, ShowLayerEntry.DefaultValues, ShowLayerEntry.ParameterOptions, context => Show(context, presenter, null)),
        Primitive<ShowColorLayerEntry>(ShowColorLayerEntry.ParameterTypes, ShowColorLayerEntry.DefaultValues, factory: context => Show(context, presenter, Arguments.String(context, "color"))),
        Primitive<HideLayerEntry>(HideLayerEntry.ParameterTypes, factory: context => Immediate(context, () =>
            BuiltinRuntimeActions.HideLayer(context.Runtime, presenter, Arguments.String(context, "handleId")))),
        Primitive<MoveLayerEntry>(MoveLayerEntry.ParameterTypes, MoveLayerEntry.DefaultValues, factory: context => Immediate(context, () =>
            BuiltinRuntimeActions.MoveLayer(
                context.Runtime,
                presenter,
                Arguments.String(context, "handleId"),
                Arguments.Json<LayerTransform>(context, "transform") ?? new LayerTransform(),
                Arguments.Float(context, "z"),
                Arguments.Float(context, "duration")))),
        Primitive<ReplaceLayerEntry>(ReplaceLayerEntry.ParameterTypes, factory: context => Immediate(context, () =>
            BuiltinRuntimeActions.ReplaceLayer(
                context.Runtime,
                presenter,
                Arguments.String(context, "handleId"),
                Arguments.String(context, "assetId"))))
    ]) { }

    private static PrimitiveInstance Show(PrimitiveCreateContext context, ILayerPresenter? presenter, string? color) =>
        Immediate(context, () =>
        {
            var request = new LayerRenderRequest(
                Arguments.String(context, "handleId"),
                Arguments.String(context, "assetId"),
                Arguments.Json<LayerTransform>(context, "transform") ?? new LayerTransform(),
                Arguments.Float(context, "z"),
                Arguments.Enum(context, "displayMode", LayerDisplayMode.Native),
                Arguments.Float(context, "opacity", 1),
                color,
                Arguments.Json<FlipbookDefinition>(context, "flipbook")?.Clone());
            BuiltinRuntimeActions.ShowLayer(context.Runtime, presenter, request);
        });

    private static PrimitiveEntryBase Primitive<TEntry>(
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? options = null,
        Func<PrimitiveCreateContext, PrimitiveInstance>? factory = null)
        where TEntry : PrimitiveEntry, new() =>
        BuiltinEntrySchemas.Primitive<TEntry>(parameters, defaults, options, factory);

    private static ImmediatePrimitiveInstance Immediate(PrimitiveCreateContext context, Action action) =>
        new(action, batchId: context.BatchId);
}

public sealed class BuiltinAnimationModule : EntryModuleBase
{
    public BuiltinAnimationModule(
        IAnimationPresenter? animationPresenter = null,
        ILayerPresenter? layerPresenter = null,
        IEffectPresenter? effectPresenter = null) : base("animation",
    [
        BuiltinEntrySchemas.Primitive<AnimateEntry>(
            AnimateEntry.ParameterTypes,
            AnimateEntry.DefaultValues,
            AnimateEntry.ParameterOptions,
            context => new AnimationPrimitiveInstance(
                context.Runtime,
                animationPresenter,
                BuiltinRuntimeActions.CreateAnimationRequest(context),
                context.BatchId,
                context.ScopeCancellation)),
        BuiltinEntrySchemas.Primitive<PlayAnimationPlanEntry>(
            PlayAnimationPlanEntry.ParameterTypes,
            factory: context => new AnimationPlanPrimitiveInstance(
                context.Runtime,
                animationPresenter,
                layerPresenter,
                effectPresenter,
                BuiltinRuntimeActions.CreateAnimationPlan(context),
                context.BatchId,
                context.ScopeCancellation)),
        BuiltinEntrySchemas.Primitive<StopAnimationEntry>(
            StopAnimationEntry.ParameterTypes,
            StopAnimationEntry.DefaultValues,
            StopAnimationEntry.ParameterOptions,
            context => new ImmediatePrimitiveInstance(() =>
            {
                var playbackHandleId = Arguments.String(context, "playbackHandleId");
                var mode = Arguments.Enum(context, "mode", AnimationStopMode.AfterIteration);
                if (context.Runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(playbackHandleId, out var playback))
                    playback.RequestStop(mode);
                if (mode == AnimationStopMode.CompleteImmediately)
                {
                    animationPresenter?.CompleteAnimationImmediately(playbackHandleId);
                    BuiltinRuntimeActions.StopAnimationState(context.Runtime, playbackHandleId);
                }
            }, batchId: context.BatchId))
    ], BuiltinTransitionEntries.Definitions) { }
}

public sealed class BuiltinAudioModule : EntryModuleBase
{
    public BuiltinAudioModule() : base("audio",
    [
        BuiltinEntrySchemas.Primitive<PlayAudioEntry>(PlayAudioEntry.ParameterTypes, PlayAudioEntry.DefaultValues, PlayAudioEntry.ParameterOptions),
        BuiltinEntrySchemas.Primitive<StopAudioEntry>(StopAudioEntry.ParameterTypes, StopAudioEntry.DefaultValues, StopAudioEntry.ParameterOptions),
        BuiltinEntrySchemas.Primitive<PauseAudioEntry>(PauseAudioEntry.ParameterTypes, PauseAudioEntry.DefaultValues, PauseAudioEntry.ParameterOptions),
        BuiltinEntrySchemas.Primitive<ResumeAudioEntry>(ResumeAudioEntry.ParameterTypes, ResumeAudioEntry.DefaultValues, ResumeAudioEntry.ParameterOptions),
        BuiltinEntrySchemas.Primitive<EnqueueAudioEntry>(EnqueueAudioEntry.ParameterTypes, EnqueueAudioEntry.DefaultValues, EnqueueAudioEntry.ParameterOptions)
    ]) { }
}

public sealed class BuiltinVideoModule : EntryModuleBase
{
    public BuiltinVideoModule() : base("video",
    [
        BuiltinEntrySchemas.Primitive<PlayVideoEntry>(PlayVideoEntry.ParameterTypes),
        BuiltinEntrySchemas.Primitive<StopVideoEntry>(StopVideoEntry.ParameterTypes)
    ]) { }
}

public sealed class BuiltinEffectModule : EntryModuleBase
{
    public BuiltinEffectModule(IEffectPresenter? presenter = null) : base("effect",
    [
        BuiltinEntrySchemas.Primitive<ApplyEffectEntry>(
            ApplyEffectEntry.ParameterTypes,
            ApplyEffectEntry.DefaultValues,
            factory: context => new ApplyEffectPrimitiveInstance(
                context.Runtime,
                presenter,
                BuiltinRuntimeActions.CreateEffectRequest(context),
                context.BatchId,
                context.ScopeCancellation)),
        BuiltinEntrySchemas.Primitive<StopEffectEntry>(
            StopEffectEntry.ParameterTypes,
            factory: context => new StopEffectPrimitiveInstance(
                context.Runtime,
                presenter,
                Arguments.String(context, "instanceId"),
                context.BatchId,
                context.ScopeCancellation))
    ]) { }
}

public sealed class BuiltinParticleModule : EntryModuleBase
{
    public BuiltinParticleModule() : base("particle",
    [
        BuiltinEntrySchemas.Primitive<PlayParticleEmitterEntry>(PlayParticleEmitterEntry.ParameterTypes, PlayParticleEmitterEntry.DefaultValues),
        BuiltinEntrySchemas.Primitive<StopParticleEmitterEntry>(StopParticleEmitterEntry.ParameterTypes)
    ]) { }
}

public sealed class BuiltinFlowModule : EntryModuleBase
{
    public BuiltinFlowModule() : base("flow",
    [
        BuiltinEntrySchemas.Primitive<WaitEntry>(
            WaitEntry.ParameterTypes,
            WaitEntry.DefaultValues,
            factory: context => new WaitPrimitiveInstance(
                TimeSpan.FromSeconds(Arguments.Float(context, "duration", 1)),
                context.BatchId,
                context.ScopeCancellation))
    ]) { }
}

public sealed class BuiltinVariableModule : EntryModuleBase
{
    public BuiltinVariableModule() : base("variable",
    [
        BuiltinEntrySchemas.Primitive<SetVariableEntry>(
            SetVariableEntry.ParameterTypes,
            factory: context => new ImmediatePrimitiveInstance(() =>
            {
                var target = Arguments.String(context, "target");
                if (!string.IsNullOrWhiteSpace(target))
                    context.Runtime.SetVariable(target, context.Runtime.EvaluateExpression(Arguments.String(context, "expression")) ?? "");
            }, batchId: context.BatchId))
    ]) { }
}

public sealed class BuiltinGalleryModule : EntryModuleBase
{
    public BuiltinGalleryModule() : base("gallery", [BuiltinEntrySchemas.Primitive<UnlockGalleryEntry>(UnlockGalleryEntry.ParameterTypes, options: UnlockGalleryEntry.ParameterOptions)]) { }
}

internal static class BuiltinEntrySchemas
{
    public static PrimitiveEntryBase Primitive<TEntry>(
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? options = null,
        Func<PrimitiveCreateContext, PrimitiveInstance>? factory = null)
        where TEntry : PrimitiveEntry, new()
    {
        var typeId = new TEntry().Type;
        return new DefaultPrimitiveEntryBase(
            typeId,
            EntrySchema.DynamicParameters(parameters, defaults, options),
            factory ?? (context => new ImmediatePrimitiveInstance(batchId: context.BatchId)));
    }
}

internal static class Arguments
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string String(PrimitiveCreateContext context, string name, string fallback = "") =>
        context.Arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    public static float Float(PrimitiveCreateContext context, string name, float fallback = 0) =>
        context.Arguments.TryGetProperty(name, out var value) && value.TryGetSingle(out var result) ? result : fallback;

    public static bool TryFloat(PrimitiveCreateContext context, string name, out float result)
    {
        if (context.Arguments.TryGetProperty(name, out var value) && value.TryGetSingle(out result))
            return true;
        result = default;
        return false;
    }

    public static int Int(PrimitiveCreateContext context, string name, int fallback = 0) =>
        context.Arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    public static bool Bool(PrimitiveCreateContext context, string name, bool fallback = false)
    {
        if (!context.Arguments.TryGetProperty(name, out var value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var result) ? result : fallback,
            _ => fallback
        };
    }

    public static TEnum Enum<TEnum>(PrimitiveCreateContext context, string name, TEnum fallback)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(String(context, name), true, out var result) ? result : fallback;

    public static string RawJson(PrimitiveCreateContext context, string name, string fallback = "{}") =>
        context.Arguments.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.GetRawText()
            : fallback;

    public static T? Json<T>(PrimitiveCreateContext context, string name)
    {
        if (!context.Arguments.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default;
        if (value.ValueKind == JsonValueKind.String)
        {
            var json = value.GetString();
            return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        return value.Deserialize<T>(JsonOptions);
    }
}
