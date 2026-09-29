using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

/// <summary>
/// Rebuilds renderer-owned scene objects from the Runtime-owned persistent scene state.
/// Live effect resources and individual particles are deliberately recreated instead of saved.
/// </summary>
public static class BuiltinPresentationReplay
{
    public static async Task ReplayPersistentSceneObjectsAsync(
        IGameRuntime runtime,
        IEffectPresenter? effectPresenter,
        IParticlePresenter? particlePresenter,
        IAnimationPresenter? animationPresenter,
        ILayerPresenter? layerPresenter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var sceneState = runtime.SceneState;

        if (effectPresenter is not null)
        {
            foreach (var effect in sceneState.ActiveEffects.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await effectPresenter.StartEffectAsync(
                    new EffectRequest(
                        effect.Id,
                        effect.InstanceId,
                        effect.TargetHandleId,
                        effect.Order,
                        effect.Parameters,
                        effect.ProgramResource)
                    {
                        AnimationValues = effect.AnimationValues.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value,
                            StringComparer.Ordinal)
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (particlePresenter is not null)
        {
            foreach (var emitter in sceneState.ActiveParticleEmitters.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await particlePresenter.StartParticleEmitterAsync(
                    new ParticleEmitterRequest(emitter.InstanceId, emitter.Definition, emitter.Z)
                    {
                        AnimationValues = emitter.AnimationValues.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value,
                            StringComparer.Ordinal)
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (animationPresenter is null)
            return;

        // Reuse the primitive lifecycle so plan events, stopping, and cleanup behave exactly like
        // a newly dispatched loop. These instances are presentation continuations, not engine flow
        // blockers, and intentionally restart without restoring an in-flight task or clock.
        foreach (var animation in sceneState.ActiveAnimations.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = animation.EntryType switch
            {
                AnimateEntry.TypeId => (PrimitiveInstance)new AnimationPrimitiveInstance(
                    runtime,
                    animationPresenter,
                    BuiltinRuntimeActions.CreateAnimationRequest(animation),
                    batchId: null,
                    cancellationToken),
                PlayAnimationPlanEntry.TypeId => new AnimationPlanPrimitiveInstance(
                    runtime,
                    animationPresenter,
                    layerPresenter,
                    effectPresenter,
                    particlePresenter,
                    BuiltinRuntimeActions.CreateAnimationPlan(animation),
                    batchId: null,
                    cancellationToken),
                _ => null
            };
            instance?.Dispatch();
        }
    }
}
