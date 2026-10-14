using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
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
        PrimitiveImplementation.For<NvlAppendOptions>(AppendNvlEntry.TypeId, (context, options) => new NvlAppendPrimitiveInstance(
                presenter ?? throw new InvalidOperationException("The NVL module has no presenter."),
                context.Runtime.TextResolver.Resolve(options.Content ?? ""),
                context.BatchId,
                context.ScopeCancellation))
    ])
    { }

    private static PrimitiveImplementation Visibility<TEntry>(INvlPresenter? presenter, Action<INvlPresenter> operation)
        where TEntry : PrimitiveEntry, new() => PrimitiveImplementation.Immediate(new TEntry().Type, _ => operation(
            presenter ?? throw new InvalidOperationException("The NVL module has no presenter.")));
}
