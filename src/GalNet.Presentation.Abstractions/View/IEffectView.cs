namespace GalNet.Core.View;

/// <summary>Runs dynamic visual effects supplied by the presentation host.</summary>
public interface IEffectView
{
    Task StartEffectAsync(EffectRequest request, CancellationToken ct);
    Task StopEffectAsync(string instanceId, CancellationToken ct);
}
