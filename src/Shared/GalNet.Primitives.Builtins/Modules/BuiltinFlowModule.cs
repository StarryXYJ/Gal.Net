using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinFlowModule() : PrimitiveModule(
    registry => registry.Register<WaitOptions>(BuiltinEntryIds.Wait, (context, options) => new WaitPrimitiveInstance(
                TimeSpan.FromSeconds(options.Duration),
                context.BatchId,
                context.ScopeCancellation)));
