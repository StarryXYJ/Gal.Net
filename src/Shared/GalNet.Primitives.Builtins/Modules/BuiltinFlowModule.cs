using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinFlowModule : PrimitiveModule
{
    public BuiltinFlowModule() : base(
    [
        BuiltinInstructionFactory.Create<WaitEntry>(context => new WaitPrimitiveInstance(
                TimeSpan.FromSeconds(Arguments.Float(context, "duration", 1)),
                context.BatchId,
                context.ScopeCancellation))
    ])
    { }
}
