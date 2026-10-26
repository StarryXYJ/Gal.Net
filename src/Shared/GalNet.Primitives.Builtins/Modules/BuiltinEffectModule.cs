using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinEffectModule(IEffectPresenter? presenter = null) : PrimitiveModule(
    registry =>
    {
        registry.State<EffectInstance>("builtin.effects", value => value.Id, async context =>
        {
            if (presenter is null) return;
            foreach (var effect in context.Runtime.State.Table<EffectInstance>().Values)
            {
                context.Own(() => presenter.StopEffectAsync(effect.Id, CancellationToken.None));
                await presenter.StartEffectAsync(new EffectRequest(effect.EffectId, effect.Id, effect.TargetHandleId,
                    effect.Order, effect.Parameters, effect.ProgramResource)
                { AnimationValues = effect.AnimationValues }, context.CancellationToken).ConfigureAwait(false);
            }
        }, keySpace: BuiltinScene.KeySpace, after: ["builtin.layers"]);
        registry.Register<EffectApplyOptions>(BuiltinEntryIds.ApplyEffect, (context, options) =>
            new ApplyEffectPrimitiveInstance(presenter, context, options));
        registry.Register<InstanceIdOptions>(BuiltinEntryIds.StopEffect, (context, options) =>
            new StopEffectPrimitiveInstance(presenter, context, options));
    });
