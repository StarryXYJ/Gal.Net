using Avalonia.Threading;
using GalNet.Control.Screen.BuiltIn;
using GalNet.Control.Widget.BuiltIn;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.View;

internal sealed class DefaultChoicePresenter
{
    private readonly GameScreenView _gameScreen;
    private readonly DefaultGameViewRegistry _registry;

    public DefaultChoicePresenter(GameScreenView gameScreen, DefaultGameViewRegistry registry)
    {
        _gameScreen = gameScreen;
        _registry = registry;
    }

    public Task<int> ShowAsync(string widgetInstanceId, string[] options, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>();

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var panel = new DefaultChoiceTemplate();
                _registry.RegisterWidget(widgetInstanceId, (AvaloniaControl)panel);

                panel.ChoiceSelected += index =>
                {
                    _gameScreen.ChoiceHost.IsVisible = false;
                    tcs.TrySetResult(index);
                };
                panel.SetChoices(options);

                _gameScreen.ChoiceHost.Content = panel;
                _gameScreen.ChoiceHost.IsVisible = true;
                _gameScreen.ClickIndicator.IsVisible = false;
            }
            catch
            {
                tcs.TrySetResult(0);
            }
        });

        ct.Register(() => tcs.TrySetCanceled());
        return tcs.Task;
    }
}
