using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GalNet.Control.Screen.Game;

namespace GalNet.Control.Runtime.Presentation;

internal sealed class DefaultChoicePresenter(GameScreenViewModel screen)
{
    public Task<int> ShowAsync(string id, string[] options, CancellationToken ct)
    {
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            var config = screen.Configuration;
            var panel = new StackPanel { Orientation = string.Equals(config.ChoiceLayout, "horizontal", StringComparison.OrdinalIgnoreCase) ? Orientation.Horizontal : Orientation.Vertical, Spacing = config.ChoiceSpacing };
            foreach (var (text, index) in options.Select((value, index) => (value, index)))
            {
                var button = new Button
                {
                    Content = text,
                    Width = config.ChoiceButtonWidth,
                    Height = config.ChoiceButtonHeight,
                    Background = new SolidColorBrush(config.ChoiceButtonColor),
                    Foreground = new SolidColorBrush(config.ChoiceButtonTextColor)
                };
                button.Click += (_, _) => { screen.IsChoiceVisible = false; result.TrySetResult(index); };
                panel.Children.Add(button);
            }
            screen.ChoiceView = panel; screen.IsChoiceVisible = true; screen.IsClickIndicatorVisible = false;
        });
        ct.Register(() => { Cancel(); result.TrySetCanceled(ct); });
        return result.Task;
    }
    public void Cancel() => Dispatcher.UIThread.Post(() => { screen.ChoiceView = null; screen.IsChoiceVisible = false; });
}
