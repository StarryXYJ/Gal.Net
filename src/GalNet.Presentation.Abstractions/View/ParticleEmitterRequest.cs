using GalNet.Core.Scene;

namespace GalNet.Core.View;

/// <summary>
/// Data passed to a platform particle-module implementation for one emitter.
/// It is not a Runtime capability interface.
/// </summary>
public sealed record ParticleEmitterRequest(string InstanceId, ParticleEmitterDefinition Definition, float Z)
{
    public IReadOnlyDictionary<string, float> AnimationValues { get; init; } = new Dictionary<string, float>(StringComparer.Ordinal);
}
