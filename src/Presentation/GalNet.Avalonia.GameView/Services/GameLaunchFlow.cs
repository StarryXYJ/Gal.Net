using GalNet.Presentation.Abstractions.Application;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Coordinates the loading screen, runtime preparation, and game-page handoff.</summary>
public sealed class GameLaunchFlow(
    Func<CancellationToken, Task> showLoading,
    Func<CancellationToken, Task> showGame,
    Func<CancellationToken, Task> showFailure)
{
    public Task StartNewGameAsync(IGameSessionService session, CancellationToken cancellationToken = default) =>
        RunSessionAsync(session, (prepared, token) => prepared.PrepareNewGameAsync(token), session.StartNewGameAsync, cancellationToken);

    public Task ContinueAsync(IGameSessionService session, CancellationToken cancellationToken = default) =>
        RunSessionAsync(session, (prepared, token) => prepared.PrepareContinueAsync(token), session.ContinueAsync, cancellationToken);

    public Task LoadAsync(IGameSessionService session, int slotIndex, CancellationToken cancellationToken = default) =>
        RunSessionAsync(session, (prepared, token) => prepared.PrepareLoadAsync(slotIndex, token),
            token => session.LoadAsync(slotIndex, token), cancellationToken);

    public Task StartNewGameAsync(IGameSessionService session, IReadOnlyDictionary<string, object> variables,
        CancellationToken cancellationToken = default)
    {
        if (session is not IParameterizedGameSessionService configured)
            throw new NotSupportedException("This session does not accept new game variables.");
        return RunAsync(token => configured.PrepareNewGameAsync(variables, token), configured.BeginPreparedGameAsync, cancellationToken);
    }

    private Task RunSessionAsync(IGameSessionService session,
        Func<IPreparedGameSessionService, CancellationToken, Task> prepare,
        Func<CancellationToken, Task> fallback, CancellationToken cancellationToken) =>
        session is IPreparedGameSessionService prepared
            ? RunAsync(token => prepare(prepared, token), prepared.BeginPreparedGameAsync, cancellationToken)
            : RunAsync(fallback, null, cancellationToken);

    public async Task RunAsync(
        Func<CancellationToken, Task> prepareGame,
        Func<CancellationToken, Task>? beginGame,
        CancellationToken cancellationToken = default)
    {
        using var flowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? preparation = null;
        try
        {
            var loadingTransition = showLoading(flowCancellation.Token);
            preparation = Task.Run(() => prepareGame(flowCancellation.Token), flowCancellation.Token);

            await loadingTransition;
            await preparation;
            cancellationToken.ThrowIfCancellationRequested();
            await showGame(cancellationToken);

            if (beginGame is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await beginGame(cancellationToken);
            }
        }
        catch
        {
            flowCancellation.Cancel();
            if (preparation is not null)
            {
                try { await preparation; }
                catch { /* Preserve the original failure after draining preparation. */ }
            }
            try
            {
                await showFailure(CancellationToken.None);
            }
            catch { }

            throw;
        }
    }
}
