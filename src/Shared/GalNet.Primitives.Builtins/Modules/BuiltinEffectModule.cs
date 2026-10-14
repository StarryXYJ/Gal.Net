using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinEffectModule : PrimitiveModule
{
    public BuiltinEffectModule(IEffectPresenter? presenter = null) : base(
    [
        BuiltinInstructionFactory.Create<ApplyEffectEntry>(context => new ApplyEffectPrimitiveInstance(
                context.Runtime,
                presenter,
                BuiltinRuntimeActions.CreateEffectRequest(context),
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
