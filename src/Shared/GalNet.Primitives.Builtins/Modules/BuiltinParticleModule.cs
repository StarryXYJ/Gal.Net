using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinParticleModule : PrimitiveModule
{
    public BuiltinParticleModule(IParticlePresenter? presenter = null) : base(
    [
        BuiltinInstructionFactory.Create<PlayParticleEmitterEntry>(context => new PlayParticlePrimitiveInstance(context.Runtime, presenter, BuiltinRuntimeActions.CreateParticleRequest(context), context.BatchId, context.ScopeCancellation)),
        BuiltinInstructionFactory.Create<BurstParticlesEntry>(context => new BurstParticlePrimitiveInstance(presenter, BuiltinRuntimeActions.CreateParticleBurstRequest(context), context.BatchId, context.ScopeCancellation)),
        BuiltinInstructionFactory.Create<StopParticleEmitterEntry>(context => new StopParticlePrimitiveInstance(context.Runtime, presenter, Arguments.String(context, "instanceId"), context.BatchId, context.ScopeCancellation))
    ])
    { }
}
