using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinNvlModule : PrimitiveModule
{
    public BuiltinNvlModule(INvlPresenter? presenter = null) : base(
    [
        Visibility<ShowNvlEntry>(presenter, static target => target.ShowNvl()),
        Visibility<HideNvlEntry>(presenter, static target => target.HideNvl()),
        Visibility<ClearNvlEntry>(presenter, static target => target.ClearNvl()),
        new PrimitiveImplementation(AppendNvlEntry.TypeId, context => new NvlAppendPrimitiveInstance(
                presenter ?? throw new InvalidOperationException("The NVL module has no presenter."),
                context.Runtime.TextResolver.Resolve(Arguments.String(context, "content")),
                context.BatchId,
                context.ScopeCancellation))
    ])
    { }

    private static PrimitiveImplementation Visibility<TEntry>(INvlPresenter? presenter, Action<INvlPresenter> operation)
        where TEntry : PrimitiveEntry, new() => new PrimitiveImplementation(new TEntry().Type, context => new ImmediatePrimitiveInstance(() => operation(
            presenter ?? throw new InvalidOperationException("The NVL module has no presenter.")), batchId: context.BatchId));
}
