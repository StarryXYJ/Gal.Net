using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinParticleModule : PrimitiveModule
{
    public BuiltinParticleModule(IParticlePresenter? presenter = null) : base(
    [
        PrimitiveImplementation.For<ParticlePlayOptions>(BuiltinEntryIds.PlayParticleEmitter, (context, options) => new PlayParticlePrimitiveInstance(context.Runtime, presenter, BuiltinRuntimeActions.CreateParticleRequest(options), context.BatchId, context.ScopeCancellation)),
        PrimitiveImplementation.For<ParticleBurstOptions>(BuiltinEntryIds.BurstParticles, (context, options) => new BurstParticlePrimitiveInstance(presenter, BuiltinRuntimeActions.CreateParticleBurstRequest(options), context.BatchId, context.ScopeCancellation)),
        PrimitiveImplementation.For<InstanceIdOptions>(BuiltinEntryIds.StopParticleEmitter, (context, options) => new StopParticlePrimitiveInstance(context.Runtime, presenter, options.InstanceId ?? "", context.BatchId, context.ScopeCancellation))
    ])
    { }
}
