using GalNet.Core.Primitives;
using GalNet.Primitives.Builtins;

namespace GeneralTest.Runtime;

public sealed class TextAdvancePrimitiveTests
{
    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    public async Task PresentationCompletionIsSeparateFromAdvanceAndStableBeforeDispatch(PrimitiveInvocationMode mode)
    {
        var text = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var instance = new Text(() => text.Task);
        var presentation = instance.PresentationCompletion;
        instance.Dispatch(mode);
        instance.Skip();
        Assert.That(instance.IsCompleted, Is.False);
        text.SetResult();
        await presentation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(instance.PresentationCompletion, Is.SameAs(presentation));
        Assert.That(instance.IsTextPresented, Is.True);
        Assert.That(instance.IsWaitingForAdvance, Is.True);
        Assert.That(instance.IsAutoBlocking, Is.False);
        Assert.That(instance.IsCompleted, Is.False);
        var completions = 0;
        instance.Completed += _ => completions++;
        instance.CompleteAdvance();
        instance.Skip();
        instance.CompleteAdvance();
        Assert.That(completions, Is.EqualTo(1));
    }

    [Test]
    public async Task CatchUpAndNaturalCompletionCanRaceWithoutDuplicateAdvance()
    {
        for (var i = 0; i < 50; i++)
        {
            var text = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var instance = new Text(() => text.Task);
            var notifications = 0;
            instance.Completed += _ => { Interlocked.Increment(ref notifications); completed.TrySetResult(); };
            instance.Dispatch();
            await Task.WhenAll(Task.Run(() => text.TrySetResult()), Task.Run(() => instance.ApplyPlaybackMode(PrimitiveInvocationMode.CatchUp)));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await instance.PresentationCompletion;
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(instance.IsTextPresented, Is.True);
        }
    }

    [Test]
    public async Task HandledFailureCompletesTheLineAndPresentation()
    {
        var instance = new Text(() => throw new IOException("text"));
        instance.Dispatch();
        await instance.PresentationCompletion;
        Assert.That(instance.IsCompleted, Is.True);
        Assert.That(instance.IsTextPresented, Is.False);
        Assert.That(instance.IsWaitingForAdvance, Is.False);
        Assert.That(instance.Failures, Is.EqualTo(1));
    }

    private sealed class Text(Func<Task> present) : TextAdvancePrimitiveInstance("batch", default)
    {
        public int Failures { get; private set; }
        protected override Task PresentTextAsync(bool immediately, CancellationToken token) => present();
        protected override void SkipText() { }
        protected override void RevealAllText() { }
        protected override void HandlePresentationFailure(Exception exception) => Failures++;
    }
}
