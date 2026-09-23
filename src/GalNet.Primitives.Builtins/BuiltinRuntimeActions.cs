using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.View;

namespace GalNet.Primitives.Builtins;

internal static class BuiltinRuntimeActions
{
    public static AnimationRequest CreateAnimationRequest(PrimitiveCreateContext context) => new()
    {
        PlaybackHandleId = Arguments.String(context, "playbackHandleId"),
        HandleId = Arguments.String(context, "handleId"),
        Property = Arguments.String(context, "property"),
        From = Arguments.TryFloat(context, "from", out var from) ? from : null,
        To = Arguments.Float(context, "to"),
        DurationSeconds = Math.Max(0, Arguments.Float(context, "duration", 0.25f)),
        Curve = AnimationCurves.Create(Arguments.Enum(context, "curve", BuiltinAnimationCurve.Linear)),
        Blocking = Arguments.Bool(context, "blocking"),
        Skippable = Arguments.Bool(context, "skippable"),
        LoopMode = Arguments.Enum(context, "loopMode", AnimationLoopMode.Once),
        BlendMode = Arguments.Enum(context, "blendMode", AnimationBlendMode.Replace)
    };

    public static AnimationPlanDefinition CreateAnimationPlan(PrimitiveCreateContext context)
    {
        var plan = Arguments.Json<AnimationPlanDefinition>(context, "plan") ?? new AnimationPlanDefinition();
        if (plan.FrameRate <= 0)
            plan.FrameRate = 60;
        if (plan.DurationFrames < 0)
            plan.DurationFrames = 0;
        return plan;
    }

    public static EffectRequest CreateEffectRequest(PrimitiveCreateContext context) => new(
        Arguments.String(context, "id"),
        Arguments.String(context, "instanceId"),
        Arguments.String(context, "targetHandleId"),
        Arguments.Int(context, "order"),
        Arguments.RawJson(context, "parameters", "{}"),
        Arguments.String(context, "program"));

    public static void ApplyEffectState(IGameRuntime runtime, EffectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId))
            return;

        RemoveEffectState(runtime, request.InstanceId);
        var effect = runtime.SceneInstances.GetOrAddTransient(request.InstanceId, _ => new EffectInstance
        {
            Id = request.InstanceId,
            EffectId = request.Id,
            ProgramResource = request.ProgramResource,
            TargetHandleId = request.TargetHandleId,
            Order = request.Order,
            Parameters = request.Parameters
        });
        effect.RestoreAnimationValues(request.AnimationValues);

        runtime.SceneState.ActiveEffects.Add(new ActiveEffectState
        {
            Id = request.Id,
            ProgramResource = request.ProgramResource,
            InstanceId = request.InstanceId,
            TargetHandleId = request.TargetHandleId,
            Order = request.Order,
            Parameters = request.Parameters,
            AnimationValues = effect.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        });
        if (!runtime.SceneState.ActiveEffectIds.Contains(request.InstanceId, StringComparer.Ordinal))
            runtime.SceneState.ActiveEffectIds.Add(request.InstanceId);
        if (!string.IsNullOrWhiteSpace(request.TargetHandleId) &&
            runtime.SceneInstances.TryGet<Layer>(request.TargetHandleId, out var layer) &&
            !layer.EffectInstanceIds.Contains(request.InstanceId, StringComparer.Ordinal))
            layer.EffectInstanceIds.Add(request.InstanceId);
    }

    public static void RemoveEffectState(IGameRuntime runtime, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return;

        runtime.SceneInstances.Remove<EffectInstance>(instanceId, out _);
        runtime.SceneState.ActiveEffectIds.RemoveAll(id => string.Equals(id, instanceId, StringComparison.Ordinal));
        runtime.SceneState.ActiveEffects.RemoveAll(effect => string.Equals(effect.InstanceId, instanceId, StringComparison.Ordinal));
        foreach (var layer in runtime.SceneState.Layers)
            layer.EffectInstanceIds.RemoveAll(id => string.Equals(id, instanceId, StringComparison.Ordinal));
    }

    public static void ApplyAnimationFinalValue(IGameRuntime runtime, AnimationRequest request)
    {
        if (request.LoopMode != AnimationLoopMode.Once)
            TrackLoopingAnimation(runtime, AnimateEntry.TypeId, request.PlaybackHandleId, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["playbackHandleId"] = request.PlaybackHandleId,
                ["handleId"] = request.HandleId,
                ["property"] = request.Property,
                ["to"] = request.To.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

        ApplyAnimationValue(runtime, request.HandleId, request.Property, request.To, request.BlendMode);
    }

    public static void ApplyPlanFinalState(IGameRuntime runtime, AnimationPlanDefinition plan)
    {
        if (plan.LoopMode != AnimationLoopMode.Once)
            TrackLoopingAnimation(runtime, PlayAnimationPlanEntry.TypeId, plan.PlaybackHandleId, new Dictionary<string, string>(StringComparer.Ordinal));

        foreach (var item in plan.Events.OrderBy(item => item.Frame))
            ApplyPlanEventToRuntime(runtime, item);

        foreach (var track in plan.Tracks)
            if (track.Keys.Count > 0)
                ApplyAnimationValue(
                    runtime,
                    track.HandleId,
                    track.Property,
                    AnimationTrackSampler.Evaluate(track, plan.DurationFrames),
                    track.BlendMode);
    }

    public static void StopAnimationState(IGameRuntime runtime, string playbackHandleId)
    {
        if (string.IsNullOrWhiteSpace(playbackHandleId))
            return;

        runtime.SceneInstances.Remove<AnimationPlaybackInstance>(playbackHandleId, out _);
        runtime.SceneState.ActiveAnimations.RemoveAll(animation =>
            string.Equals(animation.PlaybackHandleId, playbackHandleId, StringComparison.Ordinal));
    }

    public static void ApplyPlanEventToRuntime(IGameRuntime runtime, AnimationPlanEventDefinition planEvent)
    {
        switch (planEvent.Type)
        {
            case ShowLayerEntry.TypeId:
                ShowLayer(runtime, null, ToLayerRenderRequest(planEvent.Parameters, color: null), notify: false);
                break;
            case ShowColorLayerEntry.TypeId:
                ShowLayer(runtime, null, ToLayerRenderRequest(planEvent.Parameters, Get(planEvent.Parameters, "color")), notify: false);
                break;
            case HideLayerEntry.TypeId:
                HideLayer(runtime, null, Get(planEvent.Parameters, "handleId"), notify: false);
                break;
            case MoveLayerEntry.TypeId:
                MoveLayer(
                    runtime,
                    null,
                    Get(planEvent.Parameters, "handleId"),
                    Json<LayerTransform>(planEvent.Parameters, "transform") ?? new LayerTransform(),
                    Float(planEvent.Parameters, "z"),
                    Float(planEvent.Parameters, "duration"),
                    notify: false);
                break;
            case ReplaceLayerEntry.TypeId:
                ReplaceLayer(runtime, null, Get(planEvent.Parameters, "handleId"), Get(planEvent.Parameters, "assetId"), notify: false);
                break;
            case ApplyEffectEntry.TypeId:
                ApplyEffectState(runtime, ToEffectRequest(planEvent.Parameters));
                break;
            case StopEffectEntry.TypeId:
                RemoveEffectState(runtime, Get(planEvent.Parameters, "instanceId"));
                break;
        }
    }

    public static Task NotifyPlanEventAsync(
        AnimationPlanEventDefinition planEvent,
        ILayerPresenter? layerPresenter,
        IEffectPresenter? effectPresenter,
        CancellationToken cancellationToken)
    {
        switch (planEvent.Type)
        {
            case ShowLayerEntry.TypeId:
                layerPresenter?.ShowLayer(ToLayerRenderRequest(planEvent.Parameters, color: null));
                break;
            case ShowColorLayerEntry.TypeId:
                layerPresenter?.ShowLayer(ToLayerRenderRequest(planEvent.Parameters, Get(planEvent.Parameters, "color")));
                break;
            case HideLayerEntry.TypeId:
                layerPresenter?.HideLayer(Get(planEvent.Parameters, "handleId"));
                break;
            case MoveLayerEntry.TypeId:
                layerPresenter?.MoveLayer(
                    Get(planEvent.Parameters, "handleId"),
                    Json<LayerTransform>(planEvent.Parameters, "transform") ?? new LayerTransform(),
                    Float(planEvent.Parameters, "z"),
                    Float(planEvent.Parameters, "duration"));
                break;
            case ReplaceLayerEntry.TypeId:
                layerPresenter?.ReplaceLayer(Get(planEvent.Parameters, "handleId"), Get(planEvent.Parameters, "assetId"));
                break;
            case ApplyEffectEntry.TypeId when effectPresenter is not null:
                return effectPresenter.StartEffectAsync(ToEffectRequest(planEvent.Parameters), cancellationToken);
            case StopEffectEntry.TypeId when effectPresenter is not null:
                return effectPresenter.StopEffectAsync(Get(planEvent.Parameters, "instanceId"), cancellationToken);
        }

        return Task.CompletedTask;
    }

    public static void ShowLayer(IGameRuntime runtime, ILayerPresenter? presenter, LayerRenderRequest request, bool notify = true)
    {
        var layer = runtime.SceneInstances.GetOrAdd(request.HandleId, id => new Layer { Id = id });
        layer.AssetId = request.AssetId;
        layer.Color = request.Color;
        layer.Flipbook = request.Flipbook?.Clone();
        layer.Transform = request.Transform.Clone();
        layer.Z = request.Z;
        layer.Opacity = request.Opacity;
        layer.DisplayMode = request.DisplayMode;
        layer.Visible = true;
        if (notify)
            presenter?.ShowLayer(request);
    }

    public static void HideLayer(IGameRuntime runtime, ILayerPresenter? presenter, string handleId, bool notify = true)
    {
        runtime.SceneInstances.Remove<Layer>(handleId, out _);
        if (notify)
            presenter?.HideLayer(handleId);
    }

    public static void MoveLayer(
        IGameRuntime runtime,
        ILayerPresenter? presenter,
        string handleId,
        LayerTransform transform,
        float z,
        float durationSeconds,
        bool notify = true)
    {
        if (runtime.SceneInstances.TryGet<Layer>(handleId, out var layer))
        {
            layer.Transform = transform.Clone();
            layer.Z = z;
        }
        if (notify)
            presenter?.MoveLayer(handleId, transform, z, durationSeconds);
    }

    public static void ReplaceLayer(IGameRuntime runtime, ILayerPresenter? presenter, string handleId, string assetId, bool notify = true)
    {
        if (runtime.SceneInstances.TryGet<Layer>(handleId, out var layer))
        {
            layer.AssetId = assetId;
            layer.Color = null;
        }
        if (notify)
            presenter?.ReplaceLayer(handleId, assetId);
    }

    private static void ApplyAnimationValue(
        IGameRuntime runtime,
        string handleId,
        string property,
        float authoredValue,
        AnimationBlendMode blendMode)
    {
        if (!runtime.SceneInstances.TryGet<AnimatableSceneInstance>(handleId, out var target))
            return;

        var finalValue = authoredValue;
        if (blendMode == AnimationBlendMode.Additive &&
            target.TryGetAnimationValue(property, out var current))
            finalValue = current + authoredValue;

        if (target.TrySetAnimationValue(property, finalValue, out _))
            SyncEffectAnimationState(runtime, target);
    }

    private static void SyncEffectAnimationState(IGameRuntime runtime, AnimatableSceneInstance target)
    {
        if (target is not EffectInstance effect)
            return;

        var state = runtime.SceneState.ActiveEffects.FirstOrDefault(item =>
            string.Equals(item.InstanceId, effect.Id, StringComparison.Ordinal));
        if (state is null)
            return;

        state.AnimationValues.Clear();
        foreach (var (property, value) in effect.AnimationValues)
            state.AnimationValues[property] = value;
    }

    private static void TrackLoopingAnimation(
        IGameRuntime runtime,
        string entryType,
        string playbackHandleId,
        Dictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(playbackHandleId))
            return;

        runtime.SceneInstances.GetOrAddTransient(playbackHandleId, id => new AnimationPlaybackInstance
        {
            Id = id,
            LoopMode = AnimationLoopMode.Loop
        });
        runtime.SceneState.ActiveAnimations.RemoveAll(animation =>
            string.Equals(animation.PlaybackHandleId, playbackHandleId, StringComparison.Ordinal));
        runtime.SceneState.ActiveAnimations.Add(new ActiveAnimationState
        {
            EntryType = entryType,
            PlaybackHandleId = playbackHandleId,
            Parameters = parameters
        });
    }

    private static LayerRenderRequest ToLayerRenderRequest(
        IReadOnlyDictionary<string, JsonElement> parameters,
        string? color) => new(
            Get(parameters, "handleId"),
            Get(parameters, "assetId"),
            Json<LayerTransform>(parameters, "transform") ?? new LayerTransform(),
            Float(parameters, "z"),
            Enum(parameters, "displayMode", LayerDisplayMode.Native),
            Float(parameters, "opacity", 1),
            color,
            Json<FlipbookDefinition>(parameters, "flipbook")?.Clone());

    private static EffectRequest ToEffectRequest(IReadOnlyDictionary<string, JsonElement> parameters) => new(
        Get(parameters, "id"),
        Get(parameters, "instanceId"),
        Get(parameters, "targetHandleId"),
        Int(parameters, "order"),
        RawJson(parameters, "parameters", "{}"),
        Get(parameters, "program"));

    private static string Get(IReadOnlyDictionary<string, JsonElement> parameters, string name, string fallback = "") =>
        parameters.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static float Float(IReadOnlyDictionary<string, JsonElement> parameters, string name, float fallback = 0) =>
        parameters.TryGetValue(name, out var value) && value.TryGetSingle(out var result) ? result : fallback;

    private static int Int(IReadOnlyDictionary<string, JsonElement> parameters, string name, int fallback = 0) =>
        parameters.TryGetValue(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    private static TEnum Enum<TEnum>(IReadOnlyDictionary<string, JsonElement> parameters, string name, TEnum fallback)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(Get(parameters, name), true, out var result) ? result : fallback;

    private static string RawJson(IReadOnlyDictionary<string, JsonElement> parameters, string name, string fallback = "{}") =>
        parameters.TryGetValue(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.GetRawText()
            : fallback;

    private static T? Json<T>(IReadOnlyDictionary<string, JsonElement> parameters, string name) =>
        parameters.TryGetValue(name, out var value)
            ? value.Deserialize<T>(Arguments.JsonOptions)
            : default;
}
