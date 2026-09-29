using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.View;

namespace GalNet.Primitives.Builtins;

internal static class BuiltinRuntimeActions
{
    public static AnimationRequest CreateAnimationRequest(PrimitiveCreateContext context)
    {
        var curve = Arguments.Enum(context, "curve", BuiltinAnimationCurve.Linear);
        return new AnimationRequest
        {
            PlaybackHandleId = Arguments.String(context, "playbackHandleId"),
            HandleId = Arguments.String(context, "handleId"),
            Property = Arguments.String(context, "property"),
            From = Arguments.TryFloat(context, "from", out var from) ? from : null,
            To = Arguments.Float(context, "to"),
            DurationSeconds = Math.Max(0, Arguments.Float(context, "duration", 0.25f)),
            CurveKind = curve,
            Curve = AnimationCurves.Create(curve),
            Blocking = Arguments.Bool(context, "blocking"),
            Skippable = Arguments.Bool(context, "skippable"),
            LoopMode = Arguments.Enum(context, "loopMode", AnimationLoopMode.Once),
            BlendMode = Arguments.Enum(context, "blendMode", AnimationBlendMode.Replace)
        };
    }

    public static AnimationRequest CreateAnimationRequest(ActiveAnimationState state)
    {
        var curve = ParseEnum(Get(state.Parameters, "curve"), BuiltinAnimationCurve.Linear);
        return new AnimationRequest
        {
            PlaybackHandleId = state.PlaybackHandleId,
            HandleId = Get(state.Parameters, "handleId"),
            Property = Get(state.Parameters, "property"),
            From = TryParseFloat(Get(state.Parameters, "from"), out var from) ? from : null,
            To = ParseFloat(Get(state.Parameters, "to")),
            DurationSeconds = Math.Max(0, ParseDouble(Get(state.Parameters, "duration"), 0.25)),
            CurveKind = curve,
            Curve = AnimationCurves.Create(curve),
            Blocking = ParseBool(Get(state.Parameters, "blocking")),
            Skippable = ParseBool(Get(state.Parameters, "skippable")),
            LoopMode = state.LoopMode,
            BlendMode = ParseEnum(Get(state.Parameters, "blendMode"), AnimationBlendMode.Replace)
        };
    }

    public static AnimationPlanDefinition CreateAnimationPlan(PrimitiveCreateContext context)
    {
        var plan = Arguments.Json<AnimationPlanDefinition>(context, "plan") ?? new AnimationPlanDefinition();
        if (plan.FrameRate <= 0)
            plan.FrameRate = 60;
        if (plan.DurationFrames < 0)
            plan.DurationFrames = 0;
        return plan;
    }

    public static AnimationPlanDefinition CreateAnimationPlan(ActiveAnimationState state)
    {
        if (!state.Parameters.TryGetValue("plan", out var json) || string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException($"Persistent animation plan '{state.PlaybackHandleId}' has no replay definition.");
        return JsonSerializer.Deserialize<AnimationPlanDefinition>(json, Arguments.JsonOptions)
            ?? throw new InvalidDataException($"Persistent animation plan '{state.PlaybackHandleId}' is invalid.");
    }

    public static EffectRequest CreateEffectRequest(PrimitiveCreateContext context) => new(
        Arguments.String(context, "id"),
        Arguments.String(context, "instanceId"),
        Arguments.String(context, "targetHandleId"),
        Arguments.Int(context, "order"),
        Arguments.RawJson(context, "parameters", "{}"),
        Arguments.String(context, "program"));

    public static ParticleEmitterRequest CreateParticleRequest(PrimitiveCreateContext context)
    {
        var instanceId = Arguments.String(context, "instanceId");
        if (string.IsNullOrWhiteSpace(instanceId)) throw new InvalidDataException("particle.play requires 'instanceId'.");
        var definition = ParticleEmitterDefinition.FromJson(Arguments.RawJson(context, "parameters", "{}"));
        if (string.IsNullOrWhiteSpace(definition.ParticleTexture)) throw new InvalidDataException("particle.play requires 'parameters.particleTexture'.");
        return new ParticleEmitterRequest(instanceId, definition, Arguments.Float(context, "z", 100));
    }

    public static void ApplyParticleState(IGameRuntime runtime, ParticleEmitterRequest request)
    {
        RemoveParticleState(runtime, request.InstanceId);
        var emitter = runtime.SceneInstances.GetOrAddTransient(request.InstanceId, id => new ParticleEmitterInstance(id, request.Definition, request.Z));
        emitter.RestoreAnimationValues(request.AnimationValues);
        runtime.SceneState.ActiveParticleEmitters.Add(new ActiveParticleEmitterState
        {
            InstanceId = request.InstanceId, Definition = request.Definition, Z = request.Z,
            AnimationValues = emitter.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        });
    }

    public static void RemoveParticleState(IGameRuntime runtime, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        runtime.SceneInstances.Remove<ParticleEmitterInstance>(instanceId, out _);
        runtime.SceneState.ActiveParticleEmitters.RemoveAll(emitter => string.Equals(emitter.InstanceId, instanceId, StringComparison.Ordinal));
    }

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

    public static void ApplyAnimationStableState(IGameRuntime runtime, AnimationRequest request)
    {
        if (request.LoopMode == AnimationLoopMode.Once)
        {
            ApplyAnimationTerminalValue(runtime, request);
            return;
        }

        // Replace loops own a stable frame-zero value. Additive loops retain their underlying
        // base value; the presenter reapplies the authored frame-zero offset on every replay.
        if (request.BlendMode == AnimationBlendMode.Replace &&
            TryResolveAnimationStartValue(runtime, request, out var initialValue))
        {
            request.From ??= initialValue;
            ApplyAnimationValue(runtime, request.HandleId, request.Property, initialValue, AnimationBlendMode.Replace);
        }

        TrackLoopingAnimation(
            runtime,
            AnimateEntry.TypeId,
            request.PlaybackHandleId,
            request.LoopMode,
            ToReplayParameters(request));
    }

    public static void ApplyAnimationTerminalValue(IGameRuntime runtime, AnimationRequest request) =>
        ApplyAnimationValue(runtime, request.HandleId, request.Property, request.To, request.BlendMode);

    public static void ApplyPlanStableState(IGameRuntime runtime, AnimationPlanDefinition plan)
    {
        if (plan.LoopMode != AnimationLoopMode.Once)
        {
            foreach (var item in plan.Events.Where(item => item.Frame <= 0).OrderBy(item => item.Frame))
                ApplyPlanEventToRuntime(runtime, item);

            foreach (var track in plan.Tracks.Where(track => track.Keys.Count > 0 && track.BlendMode == AnimationBlendMode.Replace))
                ApplyAnimationValue(
                    runtime,
                    track.HandleId,
                    track.Property,
                    AnimationTrackSampler.Evaluate(track, 0),
                    AnimationBlendMode.Replace);

            TrackLoopingAnimation(
                runtime,
                PlayAnimationPlanEntry.TypeId,
                plan.PlaybackHandleId,
                plan.LoopMode,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["plan"] = JsonSerializer.Serialize(plan, Arguments.JsonOptions)
                });
            return;
        }

        ApplyPlanTerminalState(runtime, plan);
    }

    public static void ApplyPlanTerminalState(IGameRuntime runtime, AnimationPlanDefinition plan)
    {
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
            SyncAnimationState(runtime, target);
    }

    private static void SyncAnimationState(IGameRuntime runtime, AnimatableSceneInstance target)
    {
        switch (target)
        {
            case EffectInstance effect:
                SyncEffectAnimationState(runtime, effect);
                break;
            case ParticleEmitterInstance emitter:
                SyncParticleAnimationState(runtime, emitter);
                break;
        }
    }

    private static void SyncEffectAnimationState(IGameRuntime runtime, EffectInstance effect)
    {
        var state = runtime.SceneState.ActiveEffects.FirstOrDefault(item =>
            string.Equals(item.InstanceId, effect.Id, StringComparison.Ordinal));
        if (state is null)
            return;

        state.AnimationValues.Clear();
        foreach (var (property, value) in effect.AnimationValues)
            state.AnimationValues[property] = value;
    }

    private static void SyncParticleAnimationState(IGameRuntime runtime, ParticleEmitterInstance emitter)
    {
        var state = runtime.SceneState.ActiveParticleEmitters.FirstOrDefault(item =>
            string.Equals(item.InstanceId, emitter.Id, StringComparison.Ordinal));
        if (state is null)
            return;

        state.AnimationValues.Clear();
        foreach (var (property, value) in emitter.AnimationValues)
            state.AnimationValues[property] = value;
    }

    private static void TrackLoopingAnimation(
        IGameRuntime runtime,
        string entryType,
        string playbackHandleId,
        AnimationLoopMode loopMode,
        Dictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(playbackHandleId))
            return;

        runtime.SceneInstances.GetOrAddTransient(playbackHandleId, id => new AnimationPlaybackInstance
        {
            Id = id,
            LoopMode = loopMode
        });
        runtime.SceneState.ActiveAnimations.RemoveAll(animation =>
            string.Equals(animation.PlaybackHandleId, playbackHandleId, StringComparison.Ordinal));
        runtime.SceneState.ActiveAnimations.Add(new ActiveAnimationState
        {
            EntryType = entryType,
            PlaybackHandleId = playbackHandleId,
            LoopMode = loopMode,
            Parameters = parameters
        });
    }

    private static Dictionary<string, string> ToReplayParameters(AnimationRequest request)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["handleId"] = request.HandleId,
            ["property"] = request.Property,
            ["to"] = Invariant(request.To),
            ["duration"] = Invariant(request.DurationSeconds),
            ["curve"] = request.CurveKind.ToString(),
            ["blocking"] = request.Blocking.ToString(),
            ["skippable"] = request.Skippable.ToString(),
            ["blendMode"] = request.BlendMode.ToString()
        };
        if (request.From.HasValue)
            result["from"] = Invariant(request.From.Value);
        return result;
    }

    private static bool TryResolveAnimationStartValue(IGameRuntime runtime, AnimationRequest request, out float value)
    {
        if (request.From.HasValue)
        {
            value = request.From.Value;
            return true;
        }

        if (runtime.SceneInstances.TryGet<AnimatableSceneInstance>(request.HandleId, out var target) &&
            target.TryGetAnimationValue(request.Property, out value))
            return true;

        value = default;
        return false;
    }

    private static string Get(IReadOnlyDictionary<string, string> parameters, string name, string fallback = "") =>
        parameters.TryGetValue(name, out var value) ? value : fallback;

    private static bool TryParseFloat(string value, out float result) =>
        float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);

    private static float ParseFloat(string value, float fallback = 0) =>
        TryParseFloat(value, out var result) ? result : fallback;

    private static double ParseDouble(string value, double fallback = 0) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;

    private static bool ParseBool(string value, bool fallback = false) =>
        bool.TryParse(value, out var result) ? result : fallback;

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(value, true, out var result) ? result : fallback;

    private static string Invariant(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string Invariant(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
