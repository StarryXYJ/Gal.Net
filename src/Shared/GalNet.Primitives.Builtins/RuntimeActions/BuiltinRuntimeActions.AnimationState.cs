using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    public static void ApplyAnimationStableState(IGameRuntime runtime, AnimationRequest request)
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

        TrackLoopingAnimation(
            runtime,
            AnimateEntry.TypeId,
            request.PlaybackHandleId,
            request.LoopMode,
            ToReplayParameters(request));
    }

    public static void ApplyAnimationTerminalValue(IGameRuntime runtime, AnimationRequest request) =>
        ApplyAnimationValue(runtime, request.HandleId, request.Property, request.To, request.BlendMode);

    public static void ApplyPlanStableState(IGameRuntime runtime, AnimationPlanDefinition plan)
    {
        if (plan.LoopMode != AnimationLoopMode.Once)
        {
            foreach (var item in plan.Events.Where(item => item.Frame <= 0).OrderBy(item => item.Frame))
                ApplyPlanEventToRuntime(runtime, item);

            foreach (var track in plan.Tracks.Where(track => track.Keys.Count > 0 && track.BlendMode == AnimationBlendMode.Replace))
                ApplyAnimationValue(
                    runtime,
                    track.HandleId,
                    track.Property,
                    AnimationTrackSampler.Evaluate(track, 0),
                    AnimationBlendMode.Replace);

            TrackLoopingAnimation(
                runtime,
                PlayAnimationPlanEntry.TypeId,
                plan.PlaybackHandleId,
                plan.LoopMode,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["plan"] = JsonSerializer.Serialize(plan, Arguments.JsonOptions)
                });
            return;
        }

        ApplyPlanTerminalState(runtime, plan);
    }

    public static void ApplyPlanTerminalState(IGameRuntime runtime, AnimationPlanDefinition plan)
    {
        foreach (var item in plan.Events.OrderBy(item => item.Frame))
            ApplyPlanEventToRuntime(runtime, item);

        foreach (var track in plan.Tracks)
            if (track.Keys.Count > 0)
                ApplyAnimationValue(
                    runtime,
                    track.HandleId,
                    track.Property,
                    AnimationTrackSampler.Evaluate(track, plan.DurationFrames),
                    track.BlendMode);
    }

    public static void StopAnimationState(IGameRuntime runtime, string playbackHandleId)
    {
        if (string.IsNullOrWhiteSpace(playbackHandleId))
            return;

        runtime.SceneInstances.Remove<AnimationPlaybackInstance>(playbackHandleId, out _);
        runtime.SceneState.ActiveAnimations.RemoveAll(animation =>
            string.Equals(animation.PlaybackHandleId, playbackHandleId, StringComparison.Ordinal));
    }
}
