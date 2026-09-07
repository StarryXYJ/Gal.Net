using GalNet.Core.View;

namespace GalNet.Control.Runtime.Presentation;

/// <summary>Internal Avalonia implementation contract for a registered transition.</summary>
public interface IVisualTransition
{
    string Name { get; }
    Task ExecuteAsync(IGameView view, string? fromAsset, string? toAsset, float durationSec, CancellationToken ct);
}
