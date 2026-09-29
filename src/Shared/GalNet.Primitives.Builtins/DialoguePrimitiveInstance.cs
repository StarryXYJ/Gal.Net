using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

/// <summary>Owns typewriter and post-text waiting phases for one dialogue line.</summary>
public sealed class DialoguePrimitiveInstance : PrimitiveInstance
{
    private readonly object _gate = new();
    private readonly IDialoguePresenter _presenter;
    private readonly string _speaker;
    private readonly string _text;
    private readonly string _voice;
    private readonly CancellationToken _cancellationToken;
    private DialoguePhase _phase;

    public DialoguePrimitiveInstance(
        IDialoguePresenter presenter,
        string speaker,
        string text,
        string voice,
        string? batchId,
        CancellationToken cancellationToken)
        : base(batchId)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _speaker = speaker;
        _text = text;
        _voice = voice;
        _cancellationToken = cancellationToken;
    }

    public override bool IsBlocking => true;

    public override bool IsSkippable
    {
        get
        {
            lock (_gate)
                return _phase is DialoguePhase.Typing or DialoguePhase.WaitingAdvance;
        }
    }

    protected override void OnDispatch()
    {
        lock (_gate)
        {
            if (_phase != DialoguePhase.Created)
                throw new InvalidOperationException("Dialogue has already started.");
            _phase = DialoguePhase.Typing;
        }

        _presenter.ShowDialogue();
        if (!string.IsNullOrWhiteSpace(_voice))
            _presenter.SetVoice(_voice);
        _ = ObservePresentationAsync();
    }

    protected override void OnSkip()
    {
        var complete = false;
        lock (_gate)
        {
            switch (_phase)
            {
                case DialoguePhase.Typing:
                    _presenter.SkipText();
                    break;
                case DialoguePhase.WaitingAdvance:
                    _phase = DialoguePhase.Completed;
                    complete = true;
                    break;
            }
        }
        if (complete)
            TryComplete();
    }

    private async Task ObservePresentationAsync()
    {
        try
        {
            await _presenter.PresentTextAsync(_speaker, _text, _cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_phase == DialoguePhase.Typing)
                    _phase = DialoguePhase.WaitingAdvance;
            }
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            CompleteAfterPresentationFailure();
        }
        catch
        {
            CompleteAfterPresentationFailure();
        }
    }

    private void CompleteAfterPresentationFailure()
    {
        lock (_gate)
            _phase = DialoguePhase.Completed;
        TryComplete();
    }

    private enum DialoguePhase
    {
        Created,
        Typing,
        WaitingAdvance,
        Completed
    }
}
