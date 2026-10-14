using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinEffectModule : PrimitiveModule
{
    public BuiltinEffectModule(IEffectPresenter? presenter = null) : base(
    [
        PrimitiveImplementation.For<EffectApplyOptions>(ApplyEffectEntry.TypeId, (context, options) => new ApplyEffectPrimitiveInstance(
                context.Runtime,
                presenter,
                BuiltinRuntimeActions.CreateEffectRequest(options),
                context.BatchId,
                context.ScopeCancellation)),
        PrimitiveImplementation.For<InstanceIdOptions>(StopEffectEntry.TypeId, (context, options) => new StopEffectPrimitiveInstance(
                context.Runtime,
                presenter,
                options.InstanceId ?? "",
                context.BatchId,
                context.ScopeCancellation))
    ])
    { }
}
