namespace GalNet.Core.View;

/// <summary>Runs visual transitions supplied by the presentation host.</summary>
public interface ITransitionView
{
    Task PlayTransitionAsync(TransitionRequest request, CancellationToken ct);
}
