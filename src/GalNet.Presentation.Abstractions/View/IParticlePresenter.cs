namespace GalNet.Presentation.Abstractions.View;

/// <summary>Presentation operations used by particle primitive instances.</summary>
public interface IParticlePresenter
{
    Task StartParticleEmitterAsync(ParticleEmitterRequest request, CancellationToken cancellationToken);
    Task BurstParticlesAsync(ParticleBurstRequest request, CancellationToken cancellationToken);
    Task StopParticleEmitterAsync(string instanceId, CancellationToken cancellationToken);
}
