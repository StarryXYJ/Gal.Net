using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.Effect;

public sealed class FlashEffect : global::GalNet.Core.View.IEffect
{
    public string Name => "flash";
    private readonly Dictionary<global::GalNet.Core.View.IGameView, Border> _overlays = new();

    public void Start(global::GalNet.Core.View.IGameView view, IReadOnlyDictionary<string, object> parameters)
    {
        if (view is not AvaloniaControl target) return;

        var color = ParseColor(GetString(parameters, "color", "white"));
        var duration = GetFloat(parameters, "duration", 0.5f);

        var overlay = new Border { Background = new SolidColorBrush(color, 0.8), IsHitTestVisible = false, ZIndex = 9999 };
        var parentPanel = target as Panel ?? target.Parent as Panel;
        if (parentPanel == null) return;
        parentPanel.Children.Add(overlay);
        _overlays[view] = overlay;

        new Animation
        {
            Duration = TimeSpan.FromSeconds(duration), FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0) } }
            }
        }.RunAsync(overlay).ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RemoveOverlay(view, overlay));
        });
    }

    public void Stop(global::GalNet.Core.View.IGameView view) { if (_overlays.TryGetValue(view, out var o)) RemoveOverlay(view, o); }

    private void RemoveOverlay(global::GalNet.Core.View.IGameView view, Border overlay) { if (overlay.Parent is Panel p) p.Children.Remove(overlay); _overlays.Remove(view); }
    private static Color ParseColor(string s) => s.ToLowerInvariant() switch { "white" => Colors.White, "black" => Colors.Black, "red" => Colors.Red, _ => Color.TryParse(s, out var c) ? c : Colors.White };
    private static float GetFloat(IReadOnlyDictionary<string, object> p, string k, float d) { if (p.TryGetValue(k, out var v)) { if (v is float f) return f; if (float.TryParse(v.ToString(), out var r)) return r; } return d; }
    private static string GetString(IReadOnlyDictionary<string, object> p, string k, string d) => p.TryGetValue(k, out var v) ? v.ToString() ?? d : d;
}
