using System.Text.Json;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.View;
using GalNet.Game.Controls;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Built-in Overlay host for the CPU <c>particle.emitter</c> effect.</summary>
public sealed class AvaloniaParticleEffectView(GamePageViewModel page, IGamePageLayerFactory layers) : IEffectView, IDisposable
{
    private readonly Dictionary<string, ParticleEmitterControl> _emitters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SceneLayerItem> _blinds = new(StringComparer.Ordinal);

    public Task StartEffectAsync(EffectRequest request, CancellationToken ct)
    {
        if (string.Equals(request.Id, "mask.blinds", StringComparison.OrdinalIgnoreCase)) return StartBlindsAsync(request);
        if (!string.Equals(request.Id, "particle.emitter", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(request.InstanceId)) return Task.CompletedTask;
        return OnUiAsync(() =>
        {
            if (_emitters.Remove(request.InstanceId, out var previous)) { page.OverlayEffects.Remove(previous); previous.Dispose(); }
            var emitter = new ParticleEmitterControl(layers.ResolveLayerImage(ReadTexture(request.Parameters)), request.Parameters);
            emitter.Drained += Remove;
            _emitters.Add(request.InstanceId, emitter);
            page.OverlayEffects.Add(emitter);
        });
    }

    public Task StopEffectAsync(string instanceId, CancellationToken ct) => OnUiAsync(() =>
    {
        if (_emitters.TryGetValue(instanceId, out var emitter)) emitter.StopEmission();
        if (_blinds.Remove(instanceId, out var layer))
        {
            page.UnregisterEffectAnimation(instanceId);
            layer.BlindsBladeCount = 0;
            layer.BlindsProgress = 1;
        }
    });
    public void Dispose()
    {
        foreach (var emitter in _emitters.Values) emitter.Dispose();
        _emitters.Clear();
        foreach (var (instanceId, layer) in _blinds) { page.UnregisterEffectAnimation(instanceId); layer.BlindsBladeCount = 0; layer.BlindsProgress = 1; }
        _blinds.Clear();
    }

    private void Remove(ParticleEmitterControl emitter) => Dispatcher.UIThread.Post(() => { var pair = _emitters.FirstOrDefault(item => ReferenceEquals(item.Value, emitter)); if (!string.IsNullOrEmpty(pair.Key)) _emitters.Remove(pair.Key); page.OverlayEffects.Remove(emitter); emitter.Dispose(); });
    private Task StartBlindsAsync(EffectRequest request) => OnUiAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId) || string.IsNullOrWhiteSpace(request.TargetHandleId)) return;
        if (_blinds.Remove(request.InstanceId, out var previous)) { page.UnregisterEffectAnimation(request.InstanceId); previous.BlindsBladeCount = 0; previous.BlindsProgress = 1; }
        var layer = page.Layers.FirstOrDefault(candidate => candidate.HandleId == request.TargetHandleId);
        if (layer is null) return;
        var (count, horizontal) = ReadBlinds(request.Parameters);
        layer.BlindsBladeCount = count;
        layer.BlindsHorizontal = horizontal;
        layer.BlindsProgress = 0;
        _blinds.Add(request.InstanceId, layer);
        page.RegisterEffectProgress(request.InstanceId, value => layer.BlindsProgress = value);
    });
    private static string ReadTexture(string parameters) { try { using var doc = JsonDocument.Parse(parameters); return doc.RootElement.TryGetProperty("particleTexture", out var value) ? value.GetString() ?? "" : ""; } catch (JsonException) { return ""; } }
    private static (int count, bool horizontal) ReadBlinds(string parameters)
    {
        try
        {
            using var doc = JsonDocument.Parse(parameters);
            var count = doc.RootElement.TryGetProperty("bladeCount", out var bladeCount) && bladeCount.TryGetInt32(out var parsed) ? Math.Max(1, parsed) : 12;
            var horizontal = doc.RootElement.TryGetProperty("orientation", out var orientation) && string.Equals(orientation.GetString(), "horizontal", StringComparison.OrdinalIgnoreCase);
            return (count, horizontal);
        }
        catch (JsonException) { return (12, false); }
    }
    private static Task OnUiAsync(Action action) { if (Dispatcher.UIThread.CheckAccess()) { action(); return Task.CompletedTask; } var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Dispatcher.UIThread.Post(() => { try { action(); completion.TrySetResult(); } catch (Exception error) { completion.TrySetException(error); } }); return completion.Task; }
}
