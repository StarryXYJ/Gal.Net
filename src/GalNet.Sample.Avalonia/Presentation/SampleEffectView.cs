using GalNet.Avalonia.GameView;
using GalNet.Avalonia.GameView.Page;
using GalNet.Core.View;

namespace GalNet.Sample.Avalonia.Presentation;

/// <summary>Reference effect host. Projects can replace the dynamic dispatch table with their own effects.</summary>
internal sealed class SampleEffectView(GamePageViewModel page) : IEffectView
{
    private readonly AvaloniaEffectView _inner = new();

    public async Task StartEffectAsync(EffectRequest request, CancellationToken ct)
    {
        page.ActiveEffects.Add($"{request.Id} ({request.InstanceId})");
        await _inner.StartEffectAsync(request, ct);
    }

    public async Task StopEffectAsync(string instanceId, CancellationToken ct)
    {
        var effect = page.ActiveEffects.FirstOrDefault(value => value.EndsWith($"({instanceId})", StringComparison.Ordinal));
        if (effect is not null) page.ActiveEffects.Remove(effect);
        await _inner.StopEffectAsync(instanceId, ct);
    }
}
