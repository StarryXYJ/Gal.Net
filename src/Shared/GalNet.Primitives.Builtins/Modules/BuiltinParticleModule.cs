using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinParticleModule(IParticlePresenter? presenter = null) : PrimitiveModule(
    registry =>
    {
        registry.Register<ParticlePlayOptions>(BuiltinEntryIds.PlayParticleEmitter, (context, options) =>
            new PlayParticlePrimitiveInstance(presenter, context, options));
        registry.Register<ParticleBurstOptions>(BuiltinEntryIds.BurstParticles, (context, options) =>
            new BurstParticlePrimitiveInstance(presenter, context, options));
        registry.Register<InstanceIdOptions>(BuiltinEntryIds.StopParticleEmitter, (context, options) =>
            new StopParticlePrimitiveInstance(presenter, context, options));
    });
