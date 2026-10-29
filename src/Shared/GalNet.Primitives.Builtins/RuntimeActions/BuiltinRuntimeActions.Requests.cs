using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Primitives;
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

    public static TimelineDefinition CreateTimeline(TimelineOptions options)
    {
        return Arguments.Json<TimelineDefinition>(options.Timeline)
            ?? throw new InvalidDataException("timeline.play requires a timeline object.");
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
