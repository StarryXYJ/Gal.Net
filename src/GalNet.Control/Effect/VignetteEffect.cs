using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.Effect;

public sealed class VignetteEffect : global::GalNet.Core.View.IEffect
{
    public string Name => "vignette";
    private readonly Dictionary<global::GalNet.Core.View.IGameView, Border> _overlays = new();

    public void Start(global::GalNet.Core.View.IGameView view, IReadOnlyDictionary<string, object> parameters)
    {
        if (view is not AvaloniaControl target) return;

        var intensity = Math.Clamp(GetFloat(parameters, "intensity", 0.5f), 0, 1);
        var color = ParseColor(GetString(parameters, "color", "black"));

        var gradient = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.35),
                new GradientStop(Color.FromArgb((byte)(255 * intensity), color.R, color.G, color.B), 1.0)
            }
        };

        var overlay = new Border { Background = gradient, IsHitTestVisible = false, ZIndex = 9998 };
        var parentPanel = target as Panel ?? target.Parent as Panel;
        if (parentPanel == null) return;
        parentPanel.Children.Add(overlay);
        _overlays[view] = overlay;
    }

    public void Stop(global::GalNet.Core.View.IGameView view)
    {
        if (_overlays.TryGetValue(view, out var overlay))
        {
            if (overlay.Parent is Panel panel) panel.Children.Remove(overlay);
            _overlays.Remove(view);
        }
    }

    private static Color ParseColor(string s) => s switch { "black" => Colors.Black, "white" => Colors.White, _ => Color.TryParse(s, out var c) ? c : Colors.Black };
    private static float GetFloat(IReadOnlyDictionary<string, object> p, string k, float d) { if (p.TryGetValue(k, out var v)) { if (v is float f) return f; if (float.TryParse(v.ToString(), out var r)) return r; } return d; }
    private static string GetString(IReadOnlyDictionary<string, object> p, string k, string d) => p.TryGetValue(k, out var v) ? v.ToString() ?? d : d;
}
