using System.Text.Json;
using GalNet.Core.Scene;
using GalNet.Core.View;
using GalNet.Game.Controls.Scene;

namespace GalNet.Avalonia.GameView.Presentation;

public sealed class BlindsMaskEffectFactory : IAvaloniaEffectFactory
{
    public EffectDefinition Definition { get; } = new(
        "mask.blinds",
        EffectStage.Layer,
        [new("bladeCount", EffectParameterKind.Integer, Minimum: 1), new("orientation", EffectParameterKind.Select, Options: ["Vertical", "Horizontal"])],
        [new("progress", AnimationValueKind.Float, 0, 1)]);
    public IAvaloniaEffect Create() => new BlindsMaskEffect();
}

public sealed class BlindsMaskEffect : IAvaloniaEffect
{
    private IAvaloniaEffectHost? _host;
    private SceneLayerItem? _layer;
    private string _instanceId = "";

    public async Task StartAsync(EffectRequest request, IAvaloniaEffectHost host, CancellationToken ct)
    {
        _host = host; _instanceId = request.InstanceId;
        await host.InvokeAsync(() =>
        {
            _layer = host.FindLayer(request.TargetHandleId);
            if (_layer is null) return;
            var (count, horizontal) = ReadOptions(request.Parameters);
            _layer.BlindsBladeCount = count;
            _layer.BlindsHorizontal = horizontal;
            _layer.BlindsProgress = 0;
            host.RegisterAnimationSink(request.InstanceId, "progress", value => _layer.BlindsProgress = value);
        });
    }

    public Task StopAsync(CancellationToken ct)
    {
        var host = _host;
        return host?.InvokeAsync(() => { Reset(); host.CompleteEffect(_instanceId); }) ?? Task.CompletedTask;
    }

    public void Dispose() => Reset();

    private void Reset()
    {
        if (_host is not null) _host.UnregisterAnimationSinks(_instanceId);
        if (_layer is not null) { _layer.BlindsBladeCount = 0; _layer.BlindsProgress = 1; }
        _layer = null;
    }
    private static (int count, bool horizontal) ReadOptions(string parameters)
    {
        try
        {
            using var doc = JsonDocument.Parse(parameters);
            var count = doc.RootElement.TryGetProperty("bladeCount", out var blades) && blades.TryGetInt32(out var parsed) ? Math.Max(1, parsed) : 12;
            var horizontal = doc.RootElement.TryGetProperty("orientation", out var orientation) && string.Equals(orientation.GetString(), "horizontal", StringComparison.OrdinalIgnoreCase);
            return (count, horizontal);
        }
        catch (JsonException) { return (12, false); }
    }
}
