using System.Text.Json;
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
        ILayerPresenter? layerPresenter = null) => Array.AsReadOnly<IEntryModule>(
    [
        new BuiltinDialogueModule(dialoguePresenter),
        new BuiltinLayerModule(layerPresenter),
        new BuiltinAnimationModule(),
        new BuiltinAudioModule(),
        new BuiltinVideoModule(),
        new BuiltinEffectModule(),
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
        {
            var handleId = Arguments.String(context, "handleId");
            context.Runtime.SceneInstances.Remove<Layer>(handleId, out _);
            presenter?.HideLayer(handleId);
        })),
        Primitive<MoveLayerEntry>(MoveLayerEntry.ParameterTypes, MoveLayerEntry.DefaultValues, factory: context => Immediate(context, () =>
        {
            var handleId = Arguments.String(context, "handleId");
            var transform = Arguments.Json<LayerTransform>(context, "transform") ?? new LayerTransform();
            var z = Arguments.Float(context, "z");
            if (context.Runtime.SceneInstances.TryGet<Layer>(handleId, out var layer))
            {
                layer.Transform = transform.Clone();
                layer.Z = z;
            }
            presenter?.MoveLayer(handleId, transform, z, Arguments.Float(context, "duration"));
        })),
        Primitive<ReplaceLayerEntry>(ReplaceLayerEntry.ParameterTypes, factory: context => Immediate(context, () =>
        {
            var handleId = Arguments.String(context, "handleId");
            var assetId = Arguments.String(context, "assetId");
            if (context.Runtime.SceneInstances.TryGet<Layer>(handleId, out var layer))
            {
                layer.AssetId = assetId;
                layer.Color = null;
            }
            presenter?.ReplaceLayer(handleId, assetId);
        }))
    ]) { }

    private static PrimitiveInstance Show(PrimitiveCreateContext context, ILayerPresenter? presenter, string? color) =>
        Immediate(context, () =>
        {
            var handleId = Arguments.String(context, "handleId");
            var layer = context.Runtime.SceneInstances.GetOrAdd(handleId, id => new Layer { Id = id });
            layer.AssetId = Arguments.String(context, "assetId");
            layer.Color = color;
            layer.Flipbook = Arguments.Json<FlipbookDefinition>(context, "flipbook");
            layer.Transform = Arguments.Json<LayerTransform>(context, "transform") ?? new LayerTransform();
            layer.Z = Arguments.Float(context, "z");
            layer.Opacity = Arguments.Float(context, "opacity", 1);
            layer.DisplayMode = Arguments.Enum(context, "displayMode", LayerDisplayMode.Native);
            layer.Visible = true;
            presenter?.ShowLayer(new LayerRenderRequest(
                layer.Id,
                layer.AssetId,
                layer.Transform.Clone(),
                layer.Z,
                layer.DisplayMode,
                layer.Opacity,
                layer.Color,
                layer.Flipbook?.Clone()));
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
    public BuiltinAnimationModule() : base("animation",
    [
        BuiltinEntrySchemas.Primitive<AnimateEntry>(AnimateEntry.ParameterTypes, AnimateEntry.DefaultValues, AnimateEntry.ParameterOptions),
        BuiltinEntrySchemas.Primitive<PlayAnimationPlanEntry>(PlayAnimationPlanEntry.ParameterTypes),
        BuiltinEntrySchemas.Primitive<StopAnimationEntry>(StopAnimationEntry.ParameterTypes, StopAnimationEntry.DefaultValues, StopAnimationEntry.ParameterOptions)
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
    public BuiltinEffectModule() : base("effect",
    [
        BuiltinEntrySchemas.Primitive<ApplyEffectEntry>(ApplyEffectEntry.ParameterTypes, ApplyEffectEntry.DefaultValues),
        BuiltinEntrySchemas.Primitive<StopEffectEntry>(StopEffectEntry.ParameterTypes)
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
    public BuiltinFlowModule() : base("flow", [BuiltinEntrySchemas.Primitive<WaitEntry>(WaitEntry.ParameterTypes, WaitEntry.DefaultValues)]) { }
}

public sealed class BuiltinVariableModule : EntryModuleBase
{
    public BuiltinVariableModule() : base("variable", [BuiltinEntrySchemas.Primitive<SetVariableEntry>(SetVariableEntry.ParameterTypes)]) { }
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
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string String(PrimitiveCreateContext context, string name, string fallback = "") =>
        context.Arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    public static float Float(PrimitiveCreateContext context, string name, float fallback = 0) =>
        context.Arguments.TryGetProperty(name, out var value) && value.TryGetSingle(out var result) ? result : fallback;

    public static TEnum Enum<TEnum>(PrimitiveCreateContext context, string name, TEnum fallback)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(String(context, name), true, out var result) ? result : fallback;

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
