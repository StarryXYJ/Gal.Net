namespace GalNet.Avalonia.GameView.Services;

/// <summary>Coordinates the loading screen, runtime preparation, and game-page handoff.</summary>
public sealed class GameLaunchFlow(
    Func<CancellationToken, Task> showLoading,
    Func<CancellationToken, Task> showGame,
    Func<CancellationToken, Task> showFailure)
{
    // Keep Loading visible after its incoming CrossFade. On fast local content loads,
    // immediately starting the next CrossFade makes the page appear to flash.
    private static readonly TimeSpan MinimumLoadingDwell = TimeSpan.FromMilliseconds(350);

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
            await Task.WhenAll(preparation, Task.Delay(MinimumLoadingDwell, flowCancellation.Token));
            await showGame(cancellationToken);

            if (beginGame is not null)
                await beginGame(cancellationToken);
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
