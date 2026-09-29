using GalNet.Core.Scene;

namespace GalNet.Presentation.Abstractions.View;

/// <summary>Presentation operations used by animation primitive instances.</summary>
public interface IAnimationPresenter
{
    Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken);
    Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken cancellationToken);
    bool CompleteAnimationImmediately(string playbackHandleId);
}
