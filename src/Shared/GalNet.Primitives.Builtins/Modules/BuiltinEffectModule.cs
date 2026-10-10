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
        BuiltinInstructionFactory.Create<StopEffectEntry>(context => new StopEffectPrimitiveInstance(
                context.Runtime,
                presenter,
                Arguments.String(context, "instanceId"),
                context.BatchId,
                context.ScopeCancellation))
    ])
    { }
}
