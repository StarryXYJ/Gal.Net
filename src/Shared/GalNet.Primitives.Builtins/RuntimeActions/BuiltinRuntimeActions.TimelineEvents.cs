using GalNet.Contracts.Builtins;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    public static void ApplyTimelineEventToRuntime(IGameContext runtime, TimelineEventDefinition planEvent)
    {
        switch (planEvent.Type)
        {
            case BuiltinEntryIds.ShowLayer:
                ShowLayer(runtime, null, ToLayerRenderRequest(planEvent.Parameters, color: null), notify: false);
                break;
            case BuiltinEntryIds.ShowColorLayer:
                ShowLayer(runtime, null, ToLayerRenderRequest(planEvent.Parameters, Get(planEvent.Parameters, "color")), notify: false);
                break;
            case BuiltinEntryIds.HideLayer:
                HideLayer(runtime, null, Get(planEvent.Parameters, "handleId"), notify: false);
                break;
            case BuiltinEntryIds.MoveLayer:
                MoveLayer(
                    runtime,
                    null,
                    Get(planEvent.Parameters, "handleId"),
                    Json<LayerTransform>(planEvent.Parameters, "transform") ?? new LayerTransform(),
                    Float(planEvent.Parameters, "z"),
                    Float(planEvent.Parameters, "duration"),
                    notify: false);
                break;
            case BuiltinEntryIds.ReplaceLayer:
                ReplaceLayer(runtime, null, Get(planEvent.Parameters, "handleId"), Get(planEvent.Parameters, "assetId"), notify: false);
                break;
            case BuiltinEntryIds.ApplyEffect:
                ApplyEffectState(runtime, ToEffectRequest(planEvent.Parameters));
                break;
            case BuiltinEntryIds.StopEffect:
                RemoveEffectState(runtime, Get(planEvent.Parameters, "instanceId"));
                break;
            case BuiltinEntryIds.PlayParticleEmitter:
                ApplyParticleState(runtime, ToParticleEmitterRequest(planEvent.Parameters));
                break;
            case BuiltinEntryIds.StopParticleEmitter:
                RemoveParticleState(runtime, Get(planEvent.Parameters, "instanceId"));
                break;
        }
    }

    public static Task NotifyTimelineEventAsync(
        TimelineEventDefinition planEvent,
        ILayerPresenter? layerPresenter,
        IEffectPresenter? effectPresenter,
        IParticlePresenter? particlePresenter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (planEvent.Type)
        {
            case BuiltinEntryIds.ShowLayer:
                layerPresenter?.ShowLayer(ToLayerRenderRequest(planEvent.Parameters, color: null));
                break;
            case BuiltinEntryIds.ShowColorLayer:
                layerPresenter?.ShowLayer(ToLayerRenderRequest(planEvent.Parameters, Get(planEvent.Parameters, "color")));
                break;
            case BuiltinEntryIds.HideLayer:
                layerPresenter?.HideLayer(Get(planEvent.Parameters, "handleId"));
                break;
            case BuiltinEntryIds.MoveLayer:
                layerPresenter?.MoveLayer(
                    Get(planEvent.Parameters, "handleId"),
                    Json<LayerTransform>(planEvent.Parameters, "transform") ?? new LayerTransform(),
                    Float(planEvent.Parameters, "z"),
                    Float(planEvent.Parameters, "duration"));
                break;
            case BuiltinEntryIds.ReplaceLayer:
                layerPresenter?.ReplaceLayer(Get(planEvent.Parameters, "handleId"), Get(planEvent.Parameters, "assetId"));
                break;
            case BuiltinEntryIds.ApplyEffect when effectPresenter is not null:
                return effectPresenter.StartEffectAsync(ToEffectRequest(planEvent.Parameters), cancellationToken);
            case BuiltinEntryIds.StopEffect when effectPresenter is not null:
                return effectPresenter.StopEffectAsync(Get(planEvent.Parameters, "instanceId"), cancellationToken);
            case BuiltinEntryIds.PlayParticleEmitter when particlePresenter is not null:
                return particlePresenter.StartParticleEmitterAsync(ToParticleEmitterRequest(planEvent.Parameters), cancellationToken);
            case BuiltinEntryIds.StopParticleEmitter when particlePresenter is not null:
                return particlePresenter.StopParticleEmitterAsync(Get(planEvent.Parameters, "instanceId"), cancellationToken);
            case BuiltinEntryIds.BurstParticles when particlePresenter is not null:
                return particlePresenter.BurstParticlesAsync(ToParticleBurstRequest(planEvent.Parameters), cancellationToken);
        }

        return Task.CompletedTask;
    }
}
