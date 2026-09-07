namespace GalNet.Core.View;

/// <summary>
/// Legacy host-side transition plug-in contract. It remains during the
/// transition to <see cref="ITransitionView.PlayTransitionAsync"/>.
/// </summary>
public interface ITransition
{
    string Name { get; }
    Task ExecuteAsync(IGameView view, string? fromAsset, string? toAsset,
                      float durationSec, CancellationToken ct);
}
