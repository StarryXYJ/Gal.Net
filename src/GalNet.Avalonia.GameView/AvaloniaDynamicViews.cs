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
