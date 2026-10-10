using System.Text.Json;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
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
        return Arguments.Json<AnimationPlanDefinition>(context, "plan")
            ?? throw new InvalidDataException("animation.play requires a plan object.");
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
        var definition = ReadParticleDefinition(Arguments.RawJson(context, "parameters", "{}"), PlayParticleEmitterEntry.TypeId);
        return new ParticleEmitterRequest(instanceId, definition, Arguments.Float(context, "z", 100));
    }

    public static ParticleBurstRequest CreateParticleBurstRequest(PrimitiveCreateContext context)
    {
        var count = Arguments.Int(context, "count", 1);
        var definition = ReadParticleDefinition(Arguments.RawJson(context, "parameters", "{}"), BurstParticlesEntry.TypeId) with { EmissionRate = 0 };
        return new ParticleBurstRequest(definition, count, Arguments.Float(context, "z", 100));
    }
}
