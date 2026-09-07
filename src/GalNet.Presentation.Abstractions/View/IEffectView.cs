namespace GalNet.Core.View;

/// <summary>Runs dynamic visual effects supplied by the presentation host.</summary>
public interface IEffectView
{
    /// <summary>Starts an effect using its host-defined identifier and payload.</summary>
    Task StartEffectAsync(EffectRequest request, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Stops an effect by its host-defined instance identifier.</summary>
    Task StopEffectAsync(string instanceId, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Compatibility member used by the legacy runtime until Phase 2.</summary>
    void ApplyEffect(string effectType, IReadOnlyDictionary<string, object> parameters);

    /// <summary>Compatibility member used by the legacy runtime until Phase 2.</summary>
    void StopEffect(string effectId);

    /// <summary>Compatibility member used by the legacy runtime until Phase 2.</summary>
    void ApplyTransition(string type, float durationSec);
}
