using GalNet.Core.Entry;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.View;

namespace GalNet.Runtime.Handlers;

public sealed class PlayParticleEmitterHandler : EntryHandler
{
    public override string EntryType => PlayParticleEmitterEntry.TypeId;

    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var instanceId = context.GetString("instanceId");
        if (string.IsNullOrWhiteSpace(instanceId)) throw new InvalidDataException("particle.play requires instanceId.");
        var definition = ParticleEmitterDefinition.FromJson(context.GetString("parameters"));
        var z = context.GetFloat("z", 100);
        if (context.Runtime.SceneInstances.TryGet<ParticleEmitterInstance>(instanceId, out var existing) &&
            (existing.Definition != definition || Math.Abs(existing.Z - z) > float.Epsilon))
            throw new InvalidDataException($"Particle emitter '{instanceId}' is already active with a different definition.");

        var instance = context.Runtime.SceneInstances.GetOrAdd(instanceId, id => new ParticleEmitterInstance(id, definition, z));
        instance.IsEmitting = true;
        context.Runtime.SceneState.ActiveParticleEmitters.RemoveAll(emitter => emitter.InstanceId == instanceId);
        context.Runtime.SceneState.ActiveParticleEmitters.Add(new ActiveParticleEmitterState
        {
            InstanceId = instance.Id, Definition = instance.Definition, Z = instance.Z,
            AnimationValues = instance.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        });
        await view.StartParticleEmitterAsync(new ParticleEmitterRequest(instance.Id, instance.Definition, instance.Z)
        {
            AnimationValues = instance.AnimationValues
        }, ct);
    }
}

public sealed class StopParticleEmitterHandler : EntryHandler
{
    public override string EntryType => StopParticleEmitterEntry.TypeId;

    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var instanceId = context.GetString("instanceId");
        if (context.Runtime.SceneInstances.TryGet<ParticleEmitterInstance>(instanceId, out var emitter)) emitter.IsEmitting = false;
        // A draining renderer may remain visible briefly, but it must not be recreated from a save.
        context.Runtime.SceneState.ActiveParticleEmitters.RemoveAll(active => active.InstanceId == instanceId);
        await view.StopParticleEmitterAsync(instanceId, ct);
        context.Runtime.SceneInstances.Remove<ParticleEmitterInstance>(instanceId, out _);
    }
}

internal static class ParticleEmitterStatePersistence
{
    public static void PersistAnimationValues(IGameRuntime runtime, ParticleEmitterInstance emitter)
    {
        var state = runtime.SceneState.ActiveParticleEmitters.FirstOrDefault(active => active.InstanceId == emitter.Id);
        if (state is null) return;
        state.AnimationValues.Clear();
        foreach (var (propertyName, value) in emitter.AnimationValues) state.AnimationValues[propertyName] = value;
    }
}
