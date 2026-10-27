using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    public static void ApplyParticleState(IGameContext runtime, ParticleEmitterRequest request)
    {
        runtime.State.Update(() =>
        {

            RemoveParticleState(runtime, request.InstanceId);
            var emitter = runtime.Scene().GetOrAdd(request.InstanceId, id => new ParticleEmitterInstance(id, request.Definition, request.Z));
            emitter.RestoreAnimationValues(request.AnimationValues);

        });
    }

    public static void RemoveParticleState(IGameContext runtime, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        runtime.Scene().Remove<ParticleEmitterInstance>(instanceId, out _);
    }

    public static void ApplyEffectState(IGameContext runtime, EffectRequest request)
    {
        runtime.State.Update(() =>
        {

            if (string.IsNullOrWhiteSpace(request.InstanceId))
                return;

            RemoveEffectState(runtime, request.InstanceId);
            var effect = runtime.Scene().GetOrAdd(request.InstanceId, _ => new EffectInstance
            {
                Id = request.InstanceId,
                EffectId = request.Id,
                ProgramResource = request.ProgramResource,
                TargetHandleId = request.TargetHandleId,
                Order = request.Order,
                Parameters = request.Parameters
            });
            effect.RestoreAnimationValues(request.AnimationValues);


        });
    }

    public static void RemoveEffectState(IGameContext runtime, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return;

        runtime.Scene().Remove<EffectInstance>(instanceId, out _);
    }
}
