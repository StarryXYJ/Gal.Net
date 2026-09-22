using GalNet.Core.Primitives;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class OperationManagerTests
{
    [Test]
    public async Task SkipSelectsTheEarliestSkippableBatchAndWaitsForIt()
    {
        var manager = new OperationManager();
        var first = Track(manager, "layer.show", "opening");
        var second = Track(manager, "effect.set", "opening");
        var third = Track(manager, "animation.play", "later");

        var skip = manager.SkipNextBatchAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Control.SkipRequested.IsCompleted, Is.True);
            Assert.That(second.Control.SkipRequested.IsCompleted, Is.True);
            Assert.That(third.Control.SkipRequested.IsCompleted, Is.False);
            Assert.That(skip.IsCompleted, Is.False);
        });

        first.Completion.SetResult(PrimitiveResult.Empty);
        second.Completion.SetResult(PrimitiveResult.Empty);
        Assert.That(await skip, Is.True);

        third.Completion.SetResult(PrimitiveResult.Empty);
    }

    [Test]
    public async Task MissingBatchIdCreatesAUniqueBatch()
    {
        var manager = new OperationManager();
        var first = Track(manager, "layer.show", null);
        var second = Track(manager, "effect.set", null);

        Assert.That(first.Operation.BatchKey, Is.Not.EqualTo(second.Operation.BatchKey));

        var skip = manager.SkipNextBatchAsync();
        Assert.That(first.Control.SkipRequested.IsCompleted, Is.True);
        Assert.That(second.Control.SkipRequested.IsCompleted, Is.False);
        first.Completion.SetResult(PrimitiveResult.Empty);
        Assert.That(await skip, Is.True);
        second.Completion.SetResult(PrimitiveResult.Empty);
    }

    [Test]
    public async Task CompletedAndSkippedDispatchesAreNotTracked()
    {
        var manager = new OperationManager();
        var control = new PrimitiveExecutionControl();
        var completed = new PrimitiveDispatch(
            PrimitiveDispatchStatus.Accepted,
            new PrimitiveExecutionPolicy(false, false, null),
            Task.FromResult(PrimitiveResult.Empty));
        var skipped = new PrimitiveDispatch(
            PrimitiveDispatchStatus.Skipped,
            new PrimitiveExecutionPolicy(false, false, null),
            Task.FromResult(PrimitiveResult.Empty));

        Assert.That(manager.Track("flow.wait", completed, control), Is.Null);
        Assert.That(manager.Track("flow.wait", skipped, control), Is.Null);
        Assert.That(await manager.SkipNextBatchAsync(), Is.False);
    }

    [Test]
    public async Task CompletionRemovesTheOperation()
    {
        var manager = new OperationManager();
        var tracked = Track(manager, "animation.play", "animation");

        tracked.Completion.SetResult(PrimitiveResult.Empty);
        await AwaitConditionAsync(() => manager.ActiveOperations.Count == 0);

        Assert.That(manager.ActiveOperations, Is.Empty);
    }

    private static TrackedOperation Track(OperationManager manager, string typeId, string? batchId)
    {
        var completion = new TaskCompletionSource<PrimitiveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new PrimitiveExecutionControl();
        var operation = manager.Track(
            typeId,
            new PrimitiveDispatch(
                PrimitiveDispatchStatus.Accepted,
                new PrimitiveExecutionPolicy(false, true, batchId),
                completion.Task),
            control);

        return new TrackedOperation(operation ?? throw new AssertionException("Expected an active operation."), completion, control);
    }

    private static async Task AwaitConditionAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 10 && !condition(); attempt++)
            await Task.Yield();
    }

    private sealed record TrackedOperation(
        PrimitiveOperation Operation,
        TaskCompletionSource<PrimitiveResult> Completion,
        PrimitiveExecutionControl Control);
}
