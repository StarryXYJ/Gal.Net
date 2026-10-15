using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinEffectModule(IEffectPresenter? presenter = null) : PrimitiveModule(
    registry =>
    {
        registry.Register<EffectApplyOptions>(BuiltinEntryIds.ApplyEffect, (context, options) =>
            new ApplyEffectPrimitiveInstance(presenter, context, options));
        registry.Register<InstanceIdOptions>(BuiltinEntryIds.StopEffect, (context, options) =>
            new StopEffectPrimitiveInstance(presenter, context, options));
    });
