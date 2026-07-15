using Avalonia.Threading;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;
using GalNet.Control.ViewModels;
using GalNet.Core.Widget;

namespace GalNet.Control.View;

internal sealed class DefaultChoicePresenter
{
    private readonly DefaultGameViewRegistry _registry;
    private readonly IWidgetFactory _factory;
    private readonly WidgetBuildContext _context;
    private readonly GameScreenViewModel _screen;

    public DefaultChoicePresenter(DefaultGameViewRegistry registry, IWidgetFactory factory, WidgetBuildContext context, GameScreenViewModel screen)
    {
        _registry = registry; _factory = factory; _context = context; _screen = screen;
    }

    public Task<int> ShowAsync(string widgetInstanceId, string[] options, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var host = new WidgetHostViewModel(_factory, _context, widgetInstanceId, "choice");
                var widget = host.RequireWidget<IChoicePanel>();
                widget.ChoiceSelected += Selected;
                widget.SetChoices(options);
                _registry.RegisterWidget(widgetInstanceId, host.View!);
                _screen.ChoiceHost = host;
                _screen.IsChoiceVisible = true;
                _screen.IsClickIndicatorVisible = false;

                void Selected(int index)
                {
                    widget.ChoiceSelected -= Selected;
                    _screen.IsChoiceVisible = false;
                    tcs.TrySetResult(index);
                }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        ct.Register(() => { Cancel(); tcs.TrySetCanceled(ct); });
        return tcs.Task;
    }

    public void Cancel() => Dispatcher.UIThread.Post(() =>
    {
        _screen.ChoiceHost = null;
        _screen.IsChoiceVisible = false;
    });
}
