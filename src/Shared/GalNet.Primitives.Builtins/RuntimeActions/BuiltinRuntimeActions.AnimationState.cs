using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    public static void ApplyAnimationStableState(IGameContext runtime, AnimationRequest request)
    {
        if (request.LoopMode == AnimationLoopMode.Once)
        {
            ApplyAnimationTerminalValue(runtime, request);
            return;
        }

        // Replace loops own a stable frame-zero value. Additive loops retain their underlying
        // base value; the presenter reapplies the authored frame-zero offset on every replay.
        if (request.BlendMode == AnimationBlendMode.Replace &&
            TryResolveAnimationStartValue(runtime, request, out var initialValue))
        {
            request.From ??= initialValue;
            ApplyAnimationValue(runtime, request.HandleId, request.Property, initialValue, AnimationBlendMode.Replace);
        }

        TrackLoopingAnimation(runtime, new LoopAnimationState { Id = request.PlaybackHandleId, Request = request });
    }

    public static void ApplyAnimationTerminalValue(IGameContext runtime, AnimationRequest request) =>
        ApplyAnimationValue(runtime, request.HandleId, request.Property, request.To, request.BlendMode);

    public static void ApplyTimelineStableState(IGameContext runtime, TimelineDefinition timeline)
    {
        if (timeline.LoopMode != AnimationLoopMode.Once)
        {
            foreach (var item in timeline.Events.Where(item => item.Frame <= 0 && item.Execution != TimelineEventExecution.OnCompletion).OrderBy(item => item.Frame))
                ApplyTimelineEventToRuntime(runtime, item);

            foreach (var track in timeline.Tracks.Where(track => track.Keys.Count > 0 && track.BlendMode == AnimationBlendMode.Replace))
                ApplyAnimationValue(
                    runtime,
                    track.HandleId,
                    track.Property,
                    TimelineSampler.Evaluate(track, 0),
                    AnimationBlendMode.Replace);

            TrackLoopingAnimation(runtime, new LoopAnimationState { Id = timeline.PlaybackHandleId, Timeline = timeline });
            return;
        }

        ApplyTimelineTerminalState(runtime, timeline);
    }

    public static void ApplyTimelineTerminalState(IGameContext runtime, TimelineDefinition timeline)
    {
        foreach (var item in timeline.Events.OrderBy(item => item.Frame))
            ApplyTimelineEventToRuntime(runtime, item);

        foreach (var track in timeline.Tracks)
            if (track.Keys.Count > 0)
                ApplyAnimationValue(
                    runtime,
                    track.HandleId,
                    track.Property,
                    TimelineSampler.Evaluate(track, timeline.DurationFrames),
                    track.BlendMode);
    }

    public static void StopAnimationState(IGameContext runtime, string playbackHandleId)
    {
        if (string.IsNullOrWhiteSpace(playbackHandleId))
            return;

        runtime.State.Update(() =>
        {
            runtime.Scene().Remove<AnimationPlaybackInstance>(playbackHandleId, out _);
            runtime.State.Table<LoopAnimationState>().Remove(playbackHandleId);
        });
    }
}
