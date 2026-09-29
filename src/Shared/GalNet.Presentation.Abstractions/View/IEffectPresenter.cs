namespace GalNet.Presentation.Abstractions.View;

/// <summary>Presentation operations used by effect primitive instances.</summary>
public interface IEffectPresenter
{
    Task StartEffectAsync(EffectRequest request, CancellationToken cancellationToken);
    Task StopEffectAsync(string instanceId, CancellationToken cancellationToken);
}
