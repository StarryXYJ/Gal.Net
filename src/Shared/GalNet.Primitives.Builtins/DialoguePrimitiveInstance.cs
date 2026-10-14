using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

/// <summary>Dialogue operations for the shared text and advance phases.</summary>
public sealed class DialoguePrimitiveInstance : TextAdvancePrimitiveInstance
{
    private readonly IDialoguePresenter _presenter;
    private readonly string _speaker;
    private readonly string _text;
    public DialoguePrimitiveInstance(IDialoguePresenter presenter, string speaker, string text,
        string? batchId, CancellationToken cancellationToken) : base(batchId, cancellationToken)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _speaker = speaker;
        _text = text;
    }
    protected override void OnTextStarting() => _presenter.ShowDialogue();
    protected override Task PresentTextAsync(bool immediately, CancellationToken token) => immediately
        ? _presenter.PresentTextImmediatelyAsync(_speaker, _text, token)
        : _presenter.PresentTextAsync(_speaker, _text, token);
    protected override void SkipText() => _presenter.SkipText();
    protected override void RevealAllText() => _presenter.RevealAllText();
    protected override void RecordCatchUpText()
    {
        if (_presenter is ICatchUpTextRecorder recorder) recorder.RecordDialogue(_speaker, _text);
    }
}
