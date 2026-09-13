using GalNet.Core.Scene;

namespace GalNet.Core.View;

/// <summary>Presentation port for scene particle objects; this is intentionally separate from pixel effects.</summary>
public interface IParticleEmitterView
{
    Task StartParticleEmitterAsync(ParticleEmitterRequest request, CancellationToken ct);
    Task StopParticleEmitterAsync(string instanceId, CancellationToken ct);
}

public sealed record ParticleEmitterRequest(string InstanceId, ParticleEmitterDefinition Definition, float Z)
{
    /// <summary>Stable animation values reapplied when an emitter is recreated from a save.</summary>
    public IReadOnlyDictionary<string, float> AnimationValues { get; init; } = new Dictionary<string, float>(StringComparer.Ordinal);
}
