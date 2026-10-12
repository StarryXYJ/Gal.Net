using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

/// <summary>Owns typewriter and post-text waiting phases for one dialogue line.</summary>
public sealed class DialoguePrimitiveInstance : PrimitiveInstance, IAdvanceWaitInstance
{
    private readonly object _gate = new();
    private readonly IDialoguePresenter _presenter;
    private readonly string _speaker;
    private readonly string _text;
    private readonly CancellationToken _cancellationToken;
    private DialoguePhase _phase;
    private Task _presentationTask = Task.CompletedTask;
    public override Task PresentationCompletion => _presentationTask;
    public bool IsTextPresented { get; private set; }
    public bool IsWaitingForAdvance { get { lock (_gate) return _phase == DialoguePhase.WaitingAdvance; } }
    public void CompleteAdvance() { if (IsWaitingForAdvance) OnSkip(); }

    public DialoguePrimitiveInstance(
        IDialoguePresenter presenter,
        string speaker,
        string text,
        string? batchId,
        CancellationToken cancellationToken)
        : base(batchId)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _speaker = speaker;
        _text = text;
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
        _presentationTask = ObservePresentationAsync();
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

    protected override void OnCatchUpDispatch()
    {
        if (_presenter is ICatchUpTextRecorder recorder) recorder.RecordDialogue(_speaker, _text);
        TryComplete();
    }
    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
    {
        if (mode is PrimitiveInvocationMode.Quick or PrimitiveInvocationMode.CatchUp) _presenter.RevealAllText();
        if (mode == PrimitiveInvocationMode.CatchUp && IsWaitingForAdvance) CompleteAdvance();
    }

    private async Task ObservePresentationAsync()
    {
        try
        {
            await (InvocationMode == PrimitiveInvocationMode.Quick
                ? _presenter.PresentTextImmediatelyAsync(_speaker, _text, _cancellationToken)
                : _presenter.PresentTextAsync(_speaker, _text, _cancellationToken)).ConfigureAwait(false);
            lock (_gate)
            {
                if (_phase == DialoguePhase.Typing)
                    _phase = DialoguePhase.WaitingAdvance;
                IsTextPresented = true;
            }
            SetAutoBlocking(false);
            if (InvocationMode == PrimitiveInvocationMode.CatchUp) CompleteAdvance();
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
