using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    private static void ApplyAnimationValue(
        IGameContext runtime,
        string handleId,
        string property,
        float authoredValue,
        AnimationBlendMode blendMode)
    {
        if (!runtime.Scene().TryGet<AnimatableSceneInstance>(handleId, out var target))
            return;

        var finalValue = authoredValue;
        if (blendMode == AnimationBlendMode.Additive &&
            target.TryGetAnimationValue(property, out var current))
            finalValue = current + authoredValue;

        runtime.State.Update(() => target.TrySetAnimationValue(property, finalValue, out _));
    }

    private static void TrackLoopingAnimation(IGameContext runtime, LoopAnimationState state)
    {
        if (string.IsNullOrWhiteSpace(state.Id)) return;
        runtime.State.Update(() =>
        {
            runtime.Scene().Remove<AnimationPlaybackInstance>(state.Id, out _);
            runtime.Scene().GetOrAddTransient(state.Id, id => new AnimationPlaybackInstance
            { Id = id, LoopMode = state.Request?.LoopMode ?? state.Timeline!.LoopMode });
            runtime.State.Table<LoopAnimationState>().Set(state);
        });
    }

    private static bool TryResolveAnimationStartValue(IGameContext runtime, AnimationRequest request, out float value)
    {
        if (request.From.HasValue)
        {
            value = request.From.Value;
            return true;
        }

        if (runtime.Scene().TryGet<AnimatableSceneInstance>(request.HandleId, out var target) &&
            target.TryGetAnimationValue(request.Property, out value))
            return true;

        value = default;
        return false;
    }
}
