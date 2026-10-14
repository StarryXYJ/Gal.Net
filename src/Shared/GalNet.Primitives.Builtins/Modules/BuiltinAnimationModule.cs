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
        BuiltinInstructionFactory.Create<AnimateEntry>(context => new AnimationPrimitiveInstance(
                context.Runtime,
                animationPresenter,
                BuiltinRuntimeActions.CreateAnimationRequest(context),
                context.BatchId,
                context.ScopeCancellation)),
        BuiltinInstructionFactory.Create<PlayAnimationPlanEntry>(context => new AnimationPlanPrimitiveInstance(
                context.Runtime,
                animationPresenter,
                layerPresenter,
                effectPresenter,
                particlePresenter,
                BuiltinRuntimeActions.CreateAnimationPlan(context),
                context.BatchId,
                context.ScopeCancellation)),
        PrimitiveImplementation.Immediate<AnimationStopOptions>(StopAnimationEntry.TypeId, (context, options) =>
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
