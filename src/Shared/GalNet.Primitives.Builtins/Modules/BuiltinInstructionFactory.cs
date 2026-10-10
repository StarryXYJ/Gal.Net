using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Primitives.Builtins;

internal static class BuiltinInstructionFactory
{
    public static PrimitiveImplementation Create<TEntry>(Func<PrimitiveCreateContext, PrimitiveInstance> factory)
        where TEntry : PrimitiveEntry, new() => new(new TEntry().Type, factory);
}
