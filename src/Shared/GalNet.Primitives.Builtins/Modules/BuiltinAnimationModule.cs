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
        registry.ValidateState((_, state) =>
        {
            foreach (var animation in state.Table<LoopAnimationState>().Values)
                if (state.SceneObjects<ISceneInstance>().Any(item => item.Id == animation.Id))
                    throw new InvalidDataException($"Animation playback handle '{animation.Id}' conflicts with a scene object.");
        });
        registry.State<LoopAnimationState>("builtin.animations", value => value.Id, context =>
        {
            if (animationPresenter is null) return Task.CompletedTask;
            foreach (var state in context.Runtime.State.Table<LoopAnimationState>().Values)
            {
                if (state.Request is { } request)
                    context.Start(new AnimationPrimitiveInstance(context.Runtime, animationPresenter, request, null, context.CancellationToken, restoring: true));
                else if (state.Timeline is { } timeline)
                    context.Start(new TimelinePrimitiveInstance(context.Runtime, animationPresenter, layerPresenter, effectPresenter,
                        particlePresenter, timeline, null, context.CancellationToken, restoring: true));
            }
            return Task.CompletedTask;
        }, validate: state =>
        {
            if ((state.Request is null) == (state.Timeline is null)) throw new InvalidDataException("An animation state requires exactly one definition.");
        }, version: 2, after: ["builtin.layers", "builtin.effects", "builtin.particles", "builtin.videos"]);
        registry.Register<AnimationOptions>(BuiltinEntryIds.Animate, (context, options) =>
            new AnimationPrimitiveInstance(animationPresenter, context, options));
        registry.Register<TimelineOptions>(BuiltinEntryIds.PlayTimeline, (context, options) =>
            new TimelinePrimitiveInstance(animationPresenter, layerPresenter, effectPresenter, particlePresenter, context, options));
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
