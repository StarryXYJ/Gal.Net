using GalNet.Core.View;

namespace GalNet.Avalonia.GameView;

/// <summary>Dynamic transition dispatch point for an Avalonia game host.</summary>
public sealed class AvaloniaTransitionView : ITransitionView
{
    private readonly IReadOnlyDictionary<string, Func<TransitionRequest, CancellationToken, Task>> _handlers;

    public AvaloniaTransitionView(IReadOnlyDictionary<string, Func<TransitionRequest, CancellationToken, Task>>? handlers = null) =>
        _handlers = handlers ?? new Dictionary<string, Func<TransitionRequest, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase);

    public Task PlayTransitionAsync(TransitionRequest request, CancellationToken ct) =>
        _handlers.TryGetValue(request.Id, out var handler) ? handler(request, ct) : Task.CompletedTask;
}

/// <summary>Dynamic effect dispatch point for an Avalonia game host.</summary>
public sealed class AvaloniaEffectView : IEffectView
{
    private readonly IReadOnlyDictionary<string, Func<EffectRequest, CancellationToken, Task>> _startHandlers;
    private readonly Func<string, CancellationToken, Task> _stop;

    public AvaloniaEffectView(
        IReadOnlyDictionary<string, Func<EffectRequest, CancellationToken, Task>>? startHandlers = null,
        Func<string, CancellationToken, Task>? stop = null)
    {
        _startHandlers = startHandlers ?? new Dictionary<string, Func<EffectRequest, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase);
        _stop = stop ?? ((_, _) => Task.CompletedTask);
    }

    public Task StartEffectAsync(EffectRequest request, CancellationToken ct) =>
        _startHandlers.TryGetValue(request.Id, out var handler) ? handler(request, ct) : Task.CompletedTask;

    public Task StopEffectAsync(string instanceId, CancellationToken ct) => _stop(instanceId, ct);
}
