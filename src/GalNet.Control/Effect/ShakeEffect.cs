using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.Effect;

public sealed class ShakeEffect : global::GalNet.Core.View.IEffect
{
    public string Name => "shake";
    private readonly Dictionary<global::GalNet.Core.View.IGameView, IDisposable> _timers = new();

    public void Start(global::GalNet.Core.View.IGameView view, IReadOnlyDictionary<string, object> parameters)
    {
        if (view is not AvaloniaControl target) return;

        var intensity = GetFloat(parameters, "intensity", 5f);
        var frequency = GetFloat(parameters, "frequency", 30f);
        target.RenderTransform = new TranslateTransform(0, 0);

        var intervalMs = (int)(1000f / frequency);
        var rng = new Random();
        var cts = new CancellationTokenSource();
        var ct = cts.Token;

        var timer = new System.Timers.Timer(intervalMs);
        timer.Elapsed += (_, _) =>
        {
            if (ct.IsCancellationRequested) return;
            var ox = (rng.NextDouble() * 2 - 1) * intensity;
            var oy = (rng.NextDouble() * 2 - 1) * intensity * 0.5;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (target.RenderTransform is TranslateTransform tt) { tt.X = ox; tt.Y = oy; }
            });
        };
        timer.Start();

        _timers[view] = new Disposer(() =>
        {
            cts.Cancel(); timer.Stop(); timer.Dispose();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => target.RenderTransform = null);
        });
    }

    public void Stop(global::GalNet.Core.View.IGameView view)
    {
        if (_timers.TryGetValue(view, out var d)) { d.Dispose(); _timers.Remove(view); }
    }

    private static float GetFloat(IReadOnlyDictionary<string, object> p, string k, float d)
    {
        if (p.TryGetValue(k, out var v)) { if (v is float f) return f; if (float.TryParse(v.ToString(), out var r)) return r; }
        return d;
    }

    private sealed class Disposer(Action a) : IDisposable { public void Dispose() => a(); }
}
