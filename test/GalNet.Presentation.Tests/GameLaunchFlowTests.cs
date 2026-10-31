using GalNet.Avalonia.GameView.Services;

namespace GeneralTest.Presentation;

public sealed class GameLaunchFlowTests
{
    [Test]
    public async Task PreparationAndLoadingPresentationCompleteBeforePlaybackBegins()
    {
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shown = false;
        var began = false;
        var flow = new GameLaunchFlow(_ => loading.Task,
            _ => { shown = true; return Task.CompletedTask; },
            _ => throw new AssertionException("Successful startup must not show the failure page."));
        var run = flow.RunAsync(async _ => { preparing.SetResult(); await prepared.Task; },
            _ => { Assert.That(shown, Is.True); began = true; return Task.CompletedTask; });
        await preparing.Task;
        Assert.That(shown, Is.False);
        prepared.SetResult();
        Assert.That(began, Is.False);
        loading.SetResult();
        await run;
        Assert.That(began, Is.True);
    }

    [Test]
    public async Task FailedPresentationCancelsAndDrainsPreparationBeforeShowingFailure()
    {
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = false;
        var recovered = false;
        var flow = new GameLaunchFlow(_ => loading.Task,
            _ => throw new AssertionException("Failed loading must not show gameplay."),
            _ => { Assert.That(drained, Is.True); recovered = true; return Task.CompletedTask; });
        var run = flow.RunAsync(async token =>
        {
            preparing.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally
            {
                cleanupStarted.SetResult();
                await releaseCleanup.Task;
                drained = true;
            }
        }, null);
        await preparing.Task;
        loading.SetException(new IOException("presentation failed"));
        await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Assert.That(run.IsCompleted, Is.False, "The host must not finish while preparation cleanup is pending."); }
        finally { releaseCleanup.TrySetResult(); }
        try { await run; Assert.Fail("The startup failure must propagate."); }
        catch (IOException error) { Assert.That(error.Message, Is.EqualTo("presentation failed")); }
        Assert.That(recovered, Is.True);
    }
}
