using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
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
            case PlayParticleEmitterEntry.TypeId:
                ApplyParticleState(runtime, ToParticleEmitterRequest(planEvent.Parameters));
                break;
            case StopParticleEmitterEntry.TypeId:
                RemoveParticleState(runtime, Get(planEvent.Parameters, "instanceId"));
                break;
        }
    }

    public static Task NotifyPlanEventAsync(
        AnimationPlanEventDefinition planEvent,
        ILayerPresenter? layerPresenter,
        IEffectPresenter? effectPresenter,
        IParticlePresenter? particlePresenter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            case PlayParticleEmitterEntry.TypeId when particlePresenter is not null:
                return particlePresenter.StartParticleEmitterAsync(ToParticleEmitterRequest(planEvent.Parameters), cancellationToken);
            case StopParticleEmitterEntry.TypeId when particlePresenter is not null:
                return particlePresenter.StopParticleEmitterAsync(Get(planEvent.Parameters, "instanceId"), cancellationToken);
            case BurstParticlesEntry.TypeId when particlePresenter is not null:
                return particlePresenter.BurstParticlesAsync(ToParticleBurstRequest(planEvent.Parameters), cancellationToken);
        }

        return Task.CompletedTask;
    }
}
