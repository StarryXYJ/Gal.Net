using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinFlowModule : PrimitiveModule
{
    public BuiltinFlowModule() : base(
    [
        PrimitiveImplementation.For<WaitOptions>(WaitEntry.TypeId, (context, options) => new WaitPrimitiveInstance(
                TimeSpan.FromSeconds(options.Duration),
                context.BatchId,
                context.ScopeCancellation))
    ])
    { }
}
