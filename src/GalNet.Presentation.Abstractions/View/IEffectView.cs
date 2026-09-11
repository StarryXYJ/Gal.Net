namespace GalNet.Core.View;

/// <summary>Runs dynamic visual effects supplied by the presentation host.</summary>
public interface IEffectView
{
    /// <summary>Starts an effect; hosts must honor the supplied instance ID for later removal.</summary>
    Task StartEffectAsync(EffectRequest request, CancellationToken ct);
    Task StopEffectAsync(string instanceId, CancellationToken ct);
}
