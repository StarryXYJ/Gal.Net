using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.Transition;

public sealed class SlideRightTransition : global::GalNet.Core.View.ITransition
{
    public string Name => "slide_right";

    public async Task ExecuteAsync(global::GalNet.Core.View.IGameView view, string? fromAsset, string? toAsset,
        float durationSec, CancellationToken ct)
    {
        if (view is not AvaloniaControl target) return;

        var width = target.Bounds.Width > 0 ? target.Bounds.Width : 960;
        var half = TimeSpan.FromSeconds(durationSec / 2);

        await new Animation
        {
            Duration = half, FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0) } }
            }
        }.RunAsync(target, ct);

        target.RenderTransform = new TranslateTransform(-width, 0);
        target.Opacity = 0;

        var fadeIn = new Animation
        {
            Duration = half, FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } }
            }
        };

        var tcs = new TaskCompletionSource<bool>();
        var elapsed = TimeSpan.Zero;
        var timer = new System.Timers.Timer(16);
        timer.Elapsed += (_, _) =>
        {
            elapsed += TimeSpan.FromMilliseconds(16);
            var p = Math.Min(elapsed / half, 1.0);
            var eased = 1 - Math.Pow(1 - p, 3);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (target.RenderTransform is TranslateTransform tt) tt.X = -width * (1 - eased);
            });
            if (p >= 1.0) { timer.Stop(); timer.Dispose(); tcs.TrySetResult(true); }
        };
        timer.Start();

        await Task.WhenAll(fadeIn.RunAsync(target, ct), tcs.Task);
        target.RenderTransform = null;
    }
}
