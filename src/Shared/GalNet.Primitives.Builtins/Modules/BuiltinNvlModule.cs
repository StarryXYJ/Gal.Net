using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinNvlModule : PrimitiveModule
{
    public BuiltinNvlModule(INvlPresenter? presenter = null) : base(
    [
        Visibility(BuiltinEntryIds.ShowNvl, presenter, static target => target.ShowNvl()),
        Visibility(BuiltinEntryIds.HideNvl, presenter, static target => target.HideNvl()),
        Visibility(BuiltinEntryIds.ClearNvl, presenter, static target => target.ClearNvl()),
        PrimitiveImplementation.For<NvlAppendOptions>(BuiltinEntryIds.AppendNvl, (context, options) => new NvlAppendPrimitiveInstance(
                presenter ?? throw new InvalidOperationException("The NVL module has no presenter."),
                context.Runtime.TextResolver.Resolve(options.Content ?? ""),
                context.BatchId,
                context.ScopeCancellation))
    ])
    { }

    private static PrimitiveImplementation Visibility(string type, INvlPresenter? presenter, Action<INvlPresenter> operation)
        => PrimitiveImplementation.Immediate(type, _ => operation(
            presenter ?? throw new InvalidOperationException("The NVL module has no presenter.")));
}
