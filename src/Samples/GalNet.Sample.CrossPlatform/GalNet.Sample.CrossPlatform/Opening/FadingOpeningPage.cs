using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;

namespace GalNet.Sample.CrossPlatform.Opening;

/// <summary>Purely visual animation. The VM awaits and cancels it with all its other page work.</summary>
public class FadingOpeningPage : UserControl
{
    private FadingOpeningPageViewModel? _model;
    private readonly Func<CancellationToken, Task> _animate;

    public FadingOpeningPage()
    {
        Opacity = 0;
        _animate = AnimateAsync;
        DataContextChanged += (_, _) => BindModel();
        AttachedToVisualTree += (_, _) => BindModel();
        DetachedFromVisualTree += (_, _) => UnbindModel();
    }

    private void BindModel()
    {
        UnbindModel();
        _model = DataContext as FadingOpeningPageViewModel;
        if (_model is not null) _model.AnimateAsync = _animate;
    }

    private void UnbindModel()
    {
        if (_model?.AnimateAsync == _animate) _model.AnimateAsync = null;
        _model = null;
    }

    private Task AnimateAsync(CancellationToken token)
    {
        var model = _model ?? throw new InvalidOperationException("No opening page is attached.");
        if (model.FadeIn <= TimeSpan.Zero || model.FadeOut <= TimeSpan.Zero || model.Hold < TimeSpan.Zero)
            throw new InvalidOperationException("Opening fade durations must be positive and hold must be nonnegative.");
        var duration = model.FadeIn + model.Hold + model.FadeOut;
        var animation = new Animation
        {
            Duration = duration,
            FillMode = FillMode.Forward,
            Easing = new LinearEasing(),
            Children =
            {
                Frame(0, 0), Frame(model.FadeIn / duration, 1),
                Frame((model.FadeIn + model.Hold) / duration, 1), Frame(1, 0)
            }
        };
        return animation.RunAsync(this, token);
    }

    private static KeyFrame Frame(double cue, double opacity) => new()
    {
        Cue = new Cue(cue), Setters = { new Setter(OpacityProperty, opacity) }
    };
}
