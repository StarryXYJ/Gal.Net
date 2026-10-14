using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    public static AnimationRequest CreateAnimationRequest(AnimationOptions options, float? from)
    {
        var curve = options.Curve ?? BuiltinAnimationCurve.Linear;
        return new AnimationRequest
        {
            PlaybackHandleId = options.PlaybackHandleId ?? "",
            HandleId = options.HandleId ?? "",
            Property = options.Property ?? "",
            From = from,
            To = options.To,
            DurationSeconds = Math.Max(0, options.Duration),
            CurveKind = curve,
            Curve = AnimationCurves.Create(curve),
            Blocking = options.Blocking ?? false,
            Skippable = options.Skippable ?? false,
            LoopMode = options.LoopMode ?? AnimationLoopMode.Once,
            BlendMode = options.BlendMode ?? AnimationBlendMode.Replace
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

    public static AnimationPlanDefinition CreateAnimationPlan(AnimationPlanOptions options)
    {
        return Arguments.Json<AnimationPlanDefinition>(options.Plan)
            ?? throw new InvalidDataException("animation.play requires a plan object.");
    }

    public static AnimationPlanDefinition CreateAnimationPlan(ActiveAnimationState state)
    {
        if (!state.Parameters.TryGetValue("plan", out var json) || string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException($"Persistent animation plan '{state.PlaybackHandleId}' has no replay definition.");
        return JsonSerializer.Deserialize<AnimationPlanDefinition>(json, Arguments.JsonOptions)
            ?? throw new InvalidDataException($"Persistent animation plan '{state.PlaybackHandleId}' is invalid.");
    }

    public static EffectRequest CreateEffectRequest(EffectApplyOptions options) => new(
        options.Id ?? "", options.InstanceId ?? "", options.TargetHandleId ?? "", options.Order,
        Arguments.RawJson(options.Parameters), options.Program ?? "");

    public static ParticleEmitterRequest CreateParticleRequest(ParticlePlayOptions options)
    {
        var definition = ReadParticleDefinition(Arguments.RawJson(options.Parameters), BuiltinEntryIds.PlayParticleEmitter);
        return new ParticleEmitterRequest(options.InstanceId ?? "", definition, options.Z);
    }

    public static ParticleBurstRequest CreateParticleBurstRequest(ParticleBurstOptions options)
    {
        var definition = ReadParticleDefinition(Arguments.RawJson(options.Parameters), BuiltinEntryIds.BurstParticles) with { EmissionRate = 0 };
        return new ParticleBurstRequest(definition, options.Count, options.Z);
    }
}
