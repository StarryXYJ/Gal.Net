using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinAnimationModule : PrimitiveModule
{
    public BuiltinAnimationModule(
        IAnimationPresenter? animationPresenter = null,
        ILayerPresenter? layerPresenter = null,
        IEffectPresenter? effectPresenter = null,
        IParticlePresenter? particlePresenter = null) : base(
    [
        PrimitiveImplementation.For<AnimationOptions>(BuiltinEntryIds.Animate, (context, options) => new AnimationPrimitiveInstance(
                context.Runtime,
                animationPresenter,
                BuiltinRuntimeActions.CreateAnimationRequest(options, context.Arguments.TryGetProperty("from", out _) ? options.From : null),
                context.BatchId,
                context.ScopeCancellation)),
        PrimitiveImplementation.For<AnimationPlanOptions>(BuiltinEntryIds.PlayAnimationPlan, (context, options) => new AnimationPlanPrimitiveInstance(
                context.Runtime,
                animationPresenter,
                layerPresenter,
                effectPresenter,
                particlePresenter,
                BuiltinRuntimeActions.CreateAnimationPlan(options),
                context.BatchId,
                context.ScopeCancellation)),
        PrimitiveImplementation.Immediate<AnimationStopOptions>(BuiltinEntryIds.StopAnimation, (context, options) =>
            {
                var playbackHandleId = options.PlaybackHandleId ?? "";
                var mode = options.Mode ?? AnimationStopMode.AfterIteration;
                if (context.Runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(playbackHandleId, out var playback))
                    playback.RequestStop(mode);
                if (mode == AnimationStopMode.CompleteImmediately)
                {
                    animationPresenter?.CompleteAnimationImmediately(playbackHandleId);
                    BuiltinRuntimeActions.StopAnimationState(context.Runtime, playbackHandleId);
                }
            })
    ])
    { }
}
