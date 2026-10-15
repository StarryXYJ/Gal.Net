using GalNet.Contracts.Builtins;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinAnimationModule(
        IAnimationPresenter? animationPresenter = null,
        ILayerPresenter? layerPresenter = null,
        IEffectPresenter? effectPresenter = null,
        IParticlePresenter? particlePresenter = null) : PrimitiveModule(
    registry =>
    {
        registry.Register<AnimationOptions>(BuiltinEntryIds.Animate, (context, options) =>
            new AnimationPrimitiveInstance(animationPresenter, context, options));
        registry.Register<AnimationPlanOptions>(BuiltinEntryIds.PlayAnimationPlan, (context, options) =>
            new AnimationPlanPrimitiveInstance(animationPresenter, layerPresenter, effectPresenter, particlePresenter, context, options));
        registry.Immediate<AnimationStopOptions>(BuiltinEntryIds.StopAnimation, (context, options) =>
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
            });
    });
