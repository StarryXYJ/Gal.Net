using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.Transition;

public sealed class DissolveTransition : global::GalNet.Core.View.ITransition
{
    public string Name => "dissolve";

    public async Task ExecuteAsync(global::GalNet.Core.View.IGameView view, string? fromAsset, string? toAsset,
        float durationSec, CancellationToken ct)
    {
        if (view is not AvaloniaControl target) return;

        var half = TimeSpan.FromSeconds(durationSec / 2);

        await new Animation
        {
            Duration = half, FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.2) } }
            }
        }.RunAsync(target, ct);

        await new Animation
        {
            Duration = half, FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.2) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } }
            }
        }.RunAsync(target, ct);
    }
}
