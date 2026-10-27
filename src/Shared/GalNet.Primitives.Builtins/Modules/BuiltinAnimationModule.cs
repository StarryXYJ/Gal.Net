using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
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
                if (state.IdentityObjects<ISceneInstance>(BuiltinScene.KeySpace).Any(item => item.Id == animation.Id))
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
        registry.Async<AnimationStopOptions>(BuiltinEntryIds.StopAnimation, async (context, options, token) =>
            {
                var playbackHandleId = options.PlaybackHandleId ?? "";
                var mode = options.Mode ?? AnimationStopMode.AfterIteration;
                if (context.Runtime.Scene().TryGet<AnimationPlaybackInstance>(playbackHandleId, out var playback))
                    playback.RequestStop(mode);
                if (mode == AnimationStopMode.CompleteImmediately || playback?.PresentationCompletion.IsCompleted != false)
                {
                    animationPresenter?.CompleteAnimationImmediately(playbackHandleId);
                    if (playback is not null)
                        await playback.PresentationCompletion.WaitAsync(token).ConfigureAwait(false);
                    // Headless and catch-up playbacks can retain a loop definition without a live presenter.
                    if (context.Runtime.State.Table<LoopAnimationState>().TryGet(playbackHandleId, out var saved))
                    {
                        if (saved.Timeline is { } timeline) BuiltinRuntimeActions.ApplyTimelineTerminalState(context.Runtime, timeline);
                        else if (saved.Request is { } request) BuiltinRuntimeActions.ApplyAnimationTerminalValue(context.Runtime, request);
                    }
                    BuiltinRuntimeActions.StopAnimationState(context.Runtime, playbackHandleId);
                }
            }, blocking: true);
    });
