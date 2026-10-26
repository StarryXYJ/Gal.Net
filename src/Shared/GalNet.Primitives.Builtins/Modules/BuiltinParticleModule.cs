using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinParticleModule(IParticlePresenter? presenter = null) : PrimitiveModule(
    registry =>
    {
        registry.State<ParticleEmitterInstance>("builtin.particles", value => value.Id, async context =>
        {
            if (presenter is null) return;
            foreach (var emitter in context.Runtime.State.Table<ParticleEmitterInstance>().Values)
            {
                context.Own(() => presenter.StopParticleEmitterAsync(emitter.Id, CancellationToken.None));
                await presenter.StartParticleEmitterAsync(new ParticleEmitterRequest(emitter.Id, emitter.Definition, emitter.Z)
                { AnimationValues = emitter.AnimationValues }, context.CancellationToken).ConfigureAwait(false);
            }
        }, keySpace: BuiltinScene.KeySpace);
        registry.Register<ParticlePlayOptions>(BuiltinEntryIds.PlayParticleEmitter, (context, options) =>
            new PlayParticlePrimitiveInstance(presenter, context, options));
        registry.Register<ParticleBurstOptions>(BuiltinEntryIds.BurstParticles, (context, options) =>
            new BurstParticlePrimitiveInstance(presenter, context, options));
        registry.Register<InstanceIdOptions>(BuiltinEntryIds.StopParticleEmitter, (context, options) =>
            new StopParticlePrimitiveInstance(presenter, context, options));
    });
