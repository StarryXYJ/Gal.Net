using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class PlayParticlePrimitiveInstance(IGameRuntime runtime, IParticlePresenter? presenter,
    ParticleEmitterRequest request, string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    protected override void OnStarting() => BuiltinRuntimeActions.ApplyParticleState(runtime, request);
    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.StartParticleEmitterAsync(request, token) ?? Task.CompletedTask;
}

public sealed class StopParticlePrimitiveInstance(IGameRuntime runtime, IParticlePresenter? presenter,
    string instanceId, string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    protected override void OnStarting() => BuiltinRuntimeActions.RemoveParticleState(runtime, instanceId);
    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.StopParticleEmitterAsync(instanceId, token) ?? Task.CompletedTask;
}

public sealed class BurstParticlePrimitiveInstance(IParticlePresenter? presenter, ParticleBurstRequest request,
    string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.BurstParticlesAsync(request, token) ?? Task.CompletedTask;
}
