using GalNet.Core.Scene;

namespace GalNet.Core.View;

/// <summary>Presentation port for immediate property animation and keyframe timeline playback.</summary>
public interface IAnimationView
{
    Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken ct);
    Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken ct);
    bool SkipAnimationBatch();
}
