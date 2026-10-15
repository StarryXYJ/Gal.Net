using GalNet.Contracts.Builtins;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinNvlModule(INvlPresenter? presenter = null) : PrimitiveModule(
    registry =>
    {
        Visibility(registry, BuiltinEntryIds.ShowNvl, presenter, static target => target.ShowNvl());
        Visibility(registry, BuiltinEntryIds.HideNvl, presenter, static target => target.HideNvl());
        Visibility(registry, BuiltinEntryIds.ClearNvl, presenter, static target => target.ClearNvl());
        registry.Register<NvlAppendOptions>(BuiltinEntryIds.AppendNvl, (context, options) =>
            new NvlAppendPrimitiveInstance(presenter, context, options));
    })
{
    private static void Visibility(PrimitiveRegistry registry, string type, INvlPresenter? presenter, Action<INvlPresenter> operation)
        => registry.Immediate(type, _ => operation(
            presenter ?? throw new InvalidOperationException("The NVL module has no presenter.")));
}
