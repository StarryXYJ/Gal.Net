using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    private static void ApplyAnimationValue(
        IGameRuntime runtime,
        string handleId,
        string property,
        float authoredValue,
        AnimationBlendMode blendMode)
    {
        if (!runtime.SceneInstances.TryGet<AnimatableSceneInstance>(handleId, out var target))
            return;

        var finalValue = authoredValue;
        if (blendMode == AnimationBlendMode.Additive &&
            target.TryGetAnimationValue(property, out var current))
            finalValue = current + authoredValue;

        if (target.TrySetAnimationValue(property, finalValue, out _))
            SyncAnimationState(runtime, target);
    }

    private static void SyncAnimationState(IGameRuntime runtime, AnimatableSceneInstance target)
    {
        switch (target)
        {
            case EffectInstance effect:
                SyncEffectAnimationState(runtime, effect);
                break;
            case ParticleEmitterInstance emitter:
                SyncParticleAnimationState(runtime, emitter);
                break;
        }
    }

    private static void SyncEffectAnimationState(IGameRuntime runtime, EffectInstance effect)
    {
        var state = runtime.SceneState.ActiveEffects.FirstOrDefault(item =>
            string.Equals(item.InstanceId, effect.Id, StringComparison.Ordinal));
        if (state is null)
            return;

        state.AnimationValues.Clear();
        foreach (var (property, value) in effect.AnimationValues)
            state.AnimationValues[property] = value;
    }

    private static void SyncParticleAnimationState(IGameRuntime runtime, ParticleEmitterInstance emitter)
    {
        var state = runtime.SceneState.ActiveParticleEmitters.FirstOrDefault(item =>
            string.Equals(item.InstanceId, emitter.Id, StringComparison.Ordinal));
        if (state is null)
            return;

        state.AnimationValues.Clear();
        foreach (var (property, value) in emitter.AnimationValues)
            state.AnimationValues[property] = value;
    }

    private static void TrackLoopingAnimation(
        IGameRuntime runtime,
        string entryType,
        string playbackHandleId,
        AnimationLoopMode loopMode,
        Dictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(playbackHandleId))
            return;

        runtime.SceneInstances.Remove<AnimationPlaybackInstance>(playbackHandleId, out _);
        runtime.SceneInstances.GetOrAddTransient(playbackHandleId, id => new AnimationPlaybackInstance
        {
            Id = id,
            LoopMode = loopMode
        });
        runtime.SceneState.ActiveAnimations.RemoveAll(animation =>
            string.Equals(animation.PlaybackHandleId, playbackHandleId, StringComparison.Ordinal));
        runtime.SceneState.ActiveAnimations.Add(new ActiveAnimationState
        {
            EntryType = entryType,
            PlaybackHandleId = playbackHandleId,
            LoopMode = loopMode,
            Parameters = parameters
        });
    }

    private static Dictionary<string, string> ToReplayParameters(AnimationRequest request)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["handleId"] = request.HandleId,
            ["property"] = request.Property,
            ["to"] = Invariant(request.To),
            ["duration"] = Invariant(request.DurationSeconds),
            ["curve"] = request.CurveKind.ToString(),
            ["blocking"] = request.Blocking.ToString(),
            ["skippable"] = request.Skippable.ToString(),
            ["blendMode"] = request.BlendMode.ToString()
        };
        if (request.From.HasValue)
            result["from"] = Invariant(request.From.Value);
        return result;
    }

    private static bool TryResolveAnimationStartValue(IGameRuntime runtime, AnimationRequest request, out float value)
    {
        if (request.From.HasValue)
        {
            value = request.From.Value;
            return true;
        }

        if (runtime.SceneInstances.TryGet<AnimatableSceneInstance>(request.HandleId, out var target) &&
            target.TryGetAnimationValue(request.Property, out value))
            return true;

        value = default;
        return false;
    }
}
