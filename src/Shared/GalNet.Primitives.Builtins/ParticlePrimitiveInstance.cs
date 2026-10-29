using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class PlayParticlePrimitiveInstance(IGameContext runtime, IParticlePresenter? presenter,
    ParticleEmitterRequest request, string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    public PlayParticlePrimitiveInstance(IParticlePresenter? presenter, PrimitiveCreateContext context, ParticlePlayOptions options)
        : this(context.Runtime, presenter, BuiltinRuntimeActions.CreateParticleRequest(options), context.BatchId, context.ScopeCancellation) { }

    protected override void OnStarting() => BuiltinRuntimeActions.ApplyParticleState(runtime, request);
    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.StartParticleEmitterAsync(request, token) ?? Task.CompletedTask;
}

public sealed class StopParticlePrimitiveInstance(IGameContext runtime, IParticlePresenter? presenter,
    string instanceId, string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    public StopParticlePrimitiveInstance(IParticlePresenter? presenter, PrimitiveCreateContext context, InstanceIdOptions options)
        : this(context.Runtime, presenter, options.InstanceId ?? "", context.BatchId, context.ScopeCancellation) { }

    protected override void OnStarting() => BuiltinRuntimeActions.RemoveParticleState(runtime, instanceId);
    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.StopParticleEmitterAsync(instanceId, token) ?? Task.CompletedTask;
}

public sealed class BurstParticlePrimitiveInstance(IParticlePresenter? presenter, ParticleBurstRequest request,
    string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    public BurstParticlePrimitiveInstance(IParticlePresenter? presenter, PrimitiveCreateContext context, ParticleBurstOptions options)
        : this(presenter, BuiltinRuntimeActions.CreateParticleBurstRequest(options), context.BatchId, context.ScopeCancellation) { }

    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.BurstParticlesAsync(request, token) ?? Task.CompletedTask;
}
