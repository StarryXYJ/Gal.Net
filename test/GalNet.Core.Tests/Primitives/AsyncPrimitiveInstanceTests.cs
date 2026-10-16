using GalNet.Core.Primitives;

namespace GeneralTest.Primitives;

public sealed class AsyncPrimitiveInstanceTests
{
    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public async Task DefaultModesWaitForOperationAndCleanup(PrimitiveInvocationMode mode)
    {
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var instance = new OperationInstance(async _ =>
        {
            try { await operation.Task; }
            finally { cleanupStarted.SetResult(); await cleanup.Task; }
        });
        var completion = instance.PresentationCompletion;
        var logicalCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = 0;
        instance.Completed += _ => { notifications++; logicalCompletion.SetResult(); };
        instance.Dispatch(mode);
        instance.Skip();
        operation.SetResult();
        await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(completion.IsCompleted, Is.False);
        Assert.That(instance.IsCompleted, Is.False);
        cleanup.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        await logicalCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(instance.IsCompleted, Is.True);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(instance.PresentationCompletion, Is.SameAs(completion));
        Assert.Throws<InvalidOperationException>(() => instance.Dispatch());
    }

    [Test]
    public void CompletionObserversSeeThePublishedFailure()
    {
        var instance = new OperationInstance(_ => throw new IOException("backend"));
        var observed = false;
        instance.Completed += completed => observed = completed.PresentationCompletion.IsFaulted;
        instance.Dispatch();
        Assert.That(observed, Is.True);
        Assert.ThrowsAsync<IOException>(async () => await instance.PresentationCompletion);
    }

    [Test]
    public async Task FailuresPropagateByDefaultAndCanBeHandled()
    {
        var error = new IOException("backend");
        var failed = new OperationInstance(_ => throw error);
        failed.Dispatch();
        Assert.That(Assert.ThrowsAsync<IOException>(async () => await failed.PresentationCompletion), Is.SameAs(error));
        Assert.That(failed.IsCompleted, Is.True);
        var handled = new OperationInstance(_ => throw error, handleFailure: true);
        handled.Dispatch();
        await handled.PresentationCompletion;
        Assert.That(handled.IsCompleted, Is.True);
    }

    [Test]
    public void StateFailureStillThrowsSynchronouslyAndCompletesTask()
    {
        var instance = new OperationInstance(_ => Task.CompletedTask, starting: () => throw new InvalidDataException("state"));
        Assert.Throws<InvalidDataException>(() => instance.Dispatch());
        Assert.ThrowsAsync<InvalidDataException>(async () => await instance.PresentationCompletion);
        Assert.That(instance.IsCompleted, Is.True);
    }

    [Test]
    public async Task CancellationIsObservableAndPreservesTheToken()
    {
        using var cancellation = new CancellationTokenSource();
        var instance = new OperationInstance(token => Task.Delay(Timeout.Infinite, token), cancellationToken: cancellation.Token);
        var logicalCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.Completed += _ => logicalCompletion.TrySetResult();
        instance.Dispatch();
        cancellation.Cancel();
        try { await instance.PresentationCompletion; Assert.Fail("Expected cancellation"); }
        catch (OperationCanceledException error) { Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token)); }
        await logicalCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(instance.IsCompleted, Is.True);
    }

    private sealed class OperationInstance(Func<CancellationToken, Task> operation, bool handleFailure = false,
        Action? starting = null, CancellationToken cancellationToken = default)
        : AsyncPrimitiveInstance(blocking: true, cancellationToken: cancellationToken)
    {
        protected override void OnStarting() => starting?.Invoke();
        protected override Task ExecuteAsync(CancellationToken token) => operation(token);
        protected override bool HandleExecutionFailure(Exception exception) => handleFailure;
    }
}
