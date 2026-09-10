using GalNet.Presentation.Abstractions.Runtime;

namespace GeneralTest.Presentation;

public sealed class GameRunCoordinatorTests
{
    [Test]
    public async Task Stop_cancels_and_waits_for_the_active_flow()
    {
        using var coordinator = new GameRunCoordinator();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        coordinator.Start(async cancellationToken =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled.TrySetResult();
                throw;
            }
        });

        await coordinator.StopAsync();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.DoesNotThrowAsync(async () => await coordinator.WaitAsync());
    }

    [Test]
    public async Task Requires_the_previous_flow_to_stop_before_restart()
    {
        using var coordinator = new GameRunCoordinator();
        coordinator.Start(_ => Task.CompletedTask);

        Assert.Throws<InvalidOperationException>(() => coordinator.Start(_ => Task.CompletedTask));
        await coordinator.StopAsync();

        Assert.DoesNotThrow(() => coordinator.Start(_ => Task.CompletedTask));
    }
}
