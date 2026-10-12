using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;

namespace GeneralTest.Runtime;

public class DialoguePrimitiveInstanceTests
{
    [Test]
    public void QuickPresentsWholeTextWithoutCompletingAdvanceWait()
    {
        var presenter = new ControlledDialoguePresenter();
        var instance = new DialoguePrimitiveInstance(presenter, "Alice", "a\\skip b", null, CancellationToken.None);
        instance.Dispatch(GalNet.Core.Primitives.PrimitiveInvocationMode.Quick);
        Assert.Multiple(() =>
        {
            Assert.That(instance.IsTextPresented, Is.True);
            Assert.That(instance.IsAutoBlocking, Is.False);
            Assert.That(instance.IsWaitingForAdvance, Is.True);
            Assert.That(instance.IsCompleted, Is.False);
            Assert.That(presenter.SkipCount, Is.Zero);
        });
        instance.CompleteAdvance();
        Assert.That(instance.IsCompleted, Is.True);
    }

    [Test]
    public void CatchUpDoesNotPresentIntermediateText()
    {
        var presenter = new ControlledDialoguePresenter();
        var instance = new DialoguePrimitiveInstance(presenter, "Alice", "read", null, CancellationToken.None);
        instance.Dispatch(GalNet.Core.Primitives.PrimitiveInvocationMode.CatchUp);
        Assert.That(instance.IsCompleted, Is.True);
        Assert.That(instance.IsTextPresented, Is.False);
        Assert.That(presenter.Speaker, Is.Empty);
    }

    [Test]
    public async Task TypingSkipsDoNotCompleteDialogueAndFullTextNeedsAnotherAdvance()
    {
        var presenter = new ControlledDialoguePresenter();
        var instance = new DialoguePrimitiveInstance(
            presenter,
            "Alice",
            "one\\skip two\\skip three",
            "dialogue",
            CancellationToken.None);

        instance.Dispatch();
        instance.Skip();
        instance.Skip();

        Assert.Multiple(() =>
        {
            Assert.That(instance.IsCompleted, Is.False);
            Assert.That(presenter.SkipCount, Is.EqualTo(2));
            Assert.That(presenter.Speaker, Is.EqualTo("Alice"));
            Assert.That(instance.BatchId, Is.EqualTo("dialogue"));
        });

        presenter.FinishTyping();
        await Task.Delay(10);
        instance.Skip();

        Assert.That(instance.IsCompleted, Is.True);
    }

    [Test]
    public async Task NaturalTypingCompletionOnlyMovesToWaitingPhase()
    {
        var presenter = new ControlledDialoguePresenter();
        var instance = new DialoguePrimitiveInstance(
            presenter,
            "",
            "complete line",
            null,
            CancellationToken.None);

        instance.Dispatch();
        presenter.FinishTyping();
        await Task.Delay(10);

        Assert.That(instance.IsCompleted, Is.False);
        instance.Skip();
        Assert.That(instance.IsCompleted, Is.True);
    }

    private sealed class ControlledDialoguePresenter : IDialoguePresenter
    {
        private readonly TaskCompletionSource _typing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Speaker { get; private set; } = "";
        public int SkipCount { get; private set; }

        public void ShowDialogue() { }
        public void HideDialogue() { }

        public Task PresentTextAsync(string speaker, string text, CancellationToken cancellationToken)
        {
            Speaker = speaker;
            return _typing.Task.WaitAsync(cancellationToken);
        }

        public void SkipText() => SkipCount++;
        public void RevealAllText() => FinishTyping();
        public Task PresentTextImmediatelyAsync(string speaker, string text, CancellationToken cancellationToken)
        {
            Speaker = speaker;
            return Task.CompletedTask;
        }
        public void FinishTyping() => _typing.TrySetResult();
    }
}
