using GalNet.Presentation.Abstractions.Execution;

namespace GeneralTest.Presentation;

public sealed class PrimitiveTaskScopeTests
{
    [Test]
    public async Task FinishedOperationsDoNotAccumulateAndFailuresSurviveRemoval()
    {
        var scope = new PrimitiveTaskScope();
        for (var i = 0; i < 100; i++) await scope.Run(_ => Task.CompletedTask);
        Assert.That(scope.RunningCount, Is.Zero);
        Assert.ThrowsAsync<IOException>(async () => await scope.Run(_ => throw new IOException("first")));
        Assert.ThrowsAsync<InvalidDataException>(async () => await scope.Run(_ => throw new InvalidDataException("second")));
        Assert.That(scope.RunningCount, Is.Zero);
        var failure = Assert.ThrowsAsync<AggregateException>(async () => await scope.DisposeAsync());
        Assert.That(failure!.InnerExceptions.Select(error => error.Message), Is.EqualTo(new[] { "first", "second" }));
        Assert.ThrowsAsync<AggregateException>(async () => await scope.DisposeAsync());
    }

    [Test]
    public async Task DisposingCancelsAndWaitsForCleanupAndRejectsNewWork()
    {
        var scope = new PrimitiveTaskScope();
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = scope.Run(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cleanupStarted.SetResult(); await release.Task; }
        });
        var first = scope.DisposeAsync().AsTask();
        scope.Dispose();
        var second = scope.DisposeAsync().AsTask();
        await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(first.IsCompleted, Is.False);
        Assert.That(second, Is.SameAs(first));
        Assert.Throws<ObjectDisposedException>(() => scope.Run(_ => Task.CompletedTask));
        release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(operation.IsCanceled, Is.True);
        Assert.That(scope.RunningCount, Is.Zero);
    }

    [Test]
    public async Task CancellationCallbackFailureStillDrainsOtherOperations()
    {
        var scope = new PrimitiveTaskScope();
        using var registration = scope.CancellationToken.Register(() => throw new IOException("cancel callback"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = scope.Run(_ => release.Task);
        var disposal = scope.DisposeAsync().AsTask();
        Assert.That(disposal.IsCompleted, Is.False);
        release.SetResult();
        await operation;
        var failure = Assert.ThrowsAsync<AggregateException>(async () => await disposal);
        Assert.That(failure!.Flatten().InnerExceptions.Single().Message, Is.EqualTo("cancel callback"));
    }

    [Test]
    public async Task CompletionAndDisposalCanRace()
    {
        for (var i = 0; i < 50; i++)
        {
            var scope = new PrimitiveTaskScope();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = scope.Run(_ => release.Task);
            await Task.WhenAll(Task.Run(() => release.TrySetResult()), Task.Run(async () => await scope.DisposeAsync()));
            await operation;
            Assert.That(scope.RunningCount, Is.Zero);
        }
    }
}
