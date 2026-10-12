using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;

namespace GeneralTest.Runtime;

public sealed class CatchUpTextProjectionTests
{
    [Test]
    public void CatchUpRecordsTextWithoutPresentingOrMarkingItAsRead()
    {
        var presenter = new Recorder();
        var dialogue = new DialoguePrimitiveInstance(presenter, "Guide", "read dialogue", null, CancellationToken.None);
        var nvl = new NvlAppendPrimitiveInstance(presenter, "read NVL", null, CancellationToken.None);
        dialogue.Dispatch(PrimitiveInvocationMode.CatchUp);
        nvl.Dispatch(PrimitiveInvocationMode.CatchUp);
        Assert.That(presenter.Dialogue, Is.EqualTo(("Guide", "read dialogue")));
        Assert.That(presenter.Nvl, Is.EqualTo("read NVL"));
        Assert.That(dialogue.IsCompleted && nvl.IsCompleted, Is.True);
        Assert.That(dialogue.IsTextPresented || nvl.IsTextPresented, Is.False);
    }

    private sealed class Recorder : IDialoguePresenter, INvlPresenter, ICatchUpTextRecorder
    {
        public (string Speaker, string Text) Dialogue { get; private set; }
        public string? Nvl { get; private set; }
        public void RecordDialogue(string speaker, string text) => Dialogue = (speaker, text);
        public void RecordNvlParagraph(string text) => Nvl = text;
        public void ShowDialogue() => throw new InvalidOperationException("No intermediate presentation.");
        public void HideDialogue() => throw new InvalidOperationException("No intermediate presentation.");
        public void ShowNvl() => throw new InvalidOperationException("No intermediate presentation.");
        public void HideNvl() => throw new InvalidOperationException("No intermediate presentation.");
        public void ClearNvl() => throw new InvalidOperationException("No intermediate presentation.");
        public Task PresentTextAsync(string speaker, string text, CancellationToken cancellationToken) => throw new InvalidOperationException("No intermediate presentation.");
        public Task PresentTextImmediatelyAsync(string speaker, string text, CancellationToken cancellationToken) => throw new InvalidOperationException("No intermediate presentation.");
        public Task AppendNvlTextAsync(string text, CancellationToken cancellationToken) => throw new InvalidOperationException("No intermediate presentation.");
        public Task AppendNvlTextImmediatelyAsync(string text, CancellationToken cancellationToken) => throw new InvalidOperationException("No intermediate presentation.");
        public void SkipText() { }
        public void RevealAllText() { }
        public void SkipNvlText() { }
        public void RevealAllNvlText() { }
    }
}
