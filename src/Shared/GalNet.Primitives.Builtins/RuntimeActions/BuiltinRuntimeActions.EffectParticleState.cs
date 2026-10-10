using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    public static void ApplyParticleState(IGameRuntime runtime, ParticleEmitterRequest request)
    {
        RemoveParticleState(runtime, request.InstanceId);
        var emitter = runtime.SceneInstances.GetOrAddTransient(request.InstanceId, id => new ParticleEmitterInstance(id, request.Definition, request.Z));
        emitter.RestoreAnimationValues(request.AnimationValues);
        runtime.SceneState.ActiveParticleEmitters.Add(new ActiveParticleEmitterState
        {
            InstanceId = request.InstanceId,
            Definition = request.Definition,
            Z = request.Z,
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
    }

    public static void RemoveEffectState(IGameRuntime runtime, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return;

        runtime.SceneInstances.Remove<EffectInstance>(instanceId, out _);
        runtime.SceneState.ActiveEffects.RemoveAll(effect => string.Equals(effect.InstanceId, instanceId, StringComparison.Ordinal));
    }
}
