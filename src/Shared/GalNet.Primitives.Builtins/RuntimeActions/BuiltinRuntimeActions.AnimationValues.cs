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

        runtime.State.Update(() => target.TrySetAnimationValue(property, finalValue, out _));
    }

    private static void TrackLoopingAnimation(IGameRuntime runtime, LoopAnimationState state)
    {
        if (string.IsNullOrWhiteSpace(state.Id)) return;
        runtime.State.Update(() =>
        {
            runtime.SceneInstances.Remove<AnimationPlaybackInstance>(state.Id, out _);
            runtime.SceneInstances.GetOrAddTransient(state.Id, id => new AnimationPlaybackInstance
            { Id = id, LoopMode = state.Request?.LoopMode ?? state.Timeline!.LoopMode });
            runtime.State.Table<LoopAnimationState>().Set(state);
        });
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
