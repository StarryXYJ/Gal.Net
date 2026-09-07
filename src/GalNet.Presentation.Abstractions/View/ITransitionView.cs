namespace GalNet.Core.View;

/// <summary>Runs visual transitions supplied by the presentation host.</summary>
public interface ITransitionView
{
    /// <summary>
    /// Executes a transition. Existing presenters may rely on the default no-op
    /// implementation until their migration to <see cref="TransitionRequest"/>.
    /// </summary>
    Task PlayTransitionAsync(TransitionRequest request, CancellationToken ct) => Task.CompletedTask;
}
