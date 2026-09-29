using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Coordinates the loading screen, runtime preparation, and game-page handoff.</summary>
public sealed class GameLaunchFlow(IGameNavigationService navigation)
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
        try
        {
            var loadingTransition = navigation.ResetToAsync<LoadingPageViewModel>(
                NavigationTransition.CrossFade,
                flowCancellation.Token);
            var preparation = Task.Run(() => prepareGame(flowCancellation.Token), flowCancellation.Token);

            await loadingTransition;
            await Task.WhenAll(preparation, Task.Delay(MinimumLoadingDwell, flowCancellation.Token));
            await navigation.ResetToAsync<GamePageViewModel>(NavigationTransition.CrossFade, cancellationToken);

            if (beginGame is not null)
                await beginGame(cancellationToken);
        }
        catch
        {
            flowCancellation.Cancel();
            try
            {
                await navigation.ResetToAsync<TitlePageViewModel>(
                    NavigationTransition.CrossFade,
                    CancellationToken.None);
            }
            catch { }

            throw;
        }
    }
}
