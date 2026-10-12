using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

/// <summary>Owns the typewriter and advance phases for one appended NVL line.</summary>
public sealed class NvlAppendPrimitiveInstance : PrimitiveInstance, IAdvanceWaitInstance
{
    private readonly object _gate = new();
    private readonly INvlPresenter _presenter;
    private readonly string _text;
    private readonly CancellationToken _cancellationToken;
    private NvlAppendPhase _phase;
    private Task _presentationTask = Task.CompletedTask;
    public override Task PresentationCompletion => _presentationTask;
    public bool IsTextPresented { get; private set; }
    public bool IsWaitingForAdvance { get { lock (_gate) return _phase == NvlAppendPhase.WaitingAdvance; } }
    public void CompleteAdvance() { if (IsWaitingForAdvance) OnSkip(); }

    public NvlAppendPrimitiveInstance(
        INvlPresenter presenter,
        string text,
        string? batchId,
        CancellationToken cancellationToken)
        : base(batchId)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _text = text;
        _cancellationToken = cancellationToken;
    }

    public override bool IsBlocking => true;

    public override bool IsSkippable
    {
        get
        {
            lock (_gate)
                return _phase is NvlAppendPhase.Typing or NvlAppendPhase.WaitingAdvance;
        }
    }

    protected override void OnDispatch()
    {
        lock (_gate)
        {
            if (_phase != NvlAppendPhase.Created)
                throw new InvalidOperationException("NVL append has already started.");
            _phase = NvlAppendPhase.Typing;
        }

        _presentationTask = ObservePresentationAsync();
    }

    protected override void OnSkip()
    {
        var complete = false;
        lock (_gate)
        {
            switch (_phase)
            {
                case NvlAppendPhase.Typing:
                    _presenter.SkipNvlText();
                    break;
                case NvlAppendPhase.WaitingAdvance:
                    _phase = NvlAppendPhase.Completed;
                    complete = true;
                    break;
            }
        }
        if (complete)
            TryComplete();
    }

    protected override void OnCatchUpDispatch()
    {
        if (_presenter is ICatchUpTextRecorder recorder) recorder.RecordNvlParagraph(_text);
        TryComplete();
    }
    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
    {
        if (mode is PrimitiveInvocationMode.Quick or PrimitiveInvocationMode.CatchUp) _presenter.RevealAllNvlText();
        if (mode == PrimitiveInvocationMode.CatchUp && IsWaitingForAdvance) CompleteAdvance();
    }

    private async Task ObservePresentationAsync()
    {
        try
        {
            await (InvocationMode == PrimitiveInvocationMode.Quick
                ? _presenter.AppendNvlTextImmediatelyAsync(_text, _cancellationToken)
                : _presenter.AppendNvlTextAsync(_text, _cancellationToken)).ConfigureAwait(false);
            lock (_gate)
            {
                if (_phase == NvlAppendPhase.Typing)
                    _phase = NvlAppendPhase.WaitingAdvance;
                IsTextPresented = true;
            }
            SetAutoBlocking(false);
            if (InvocationMode == PrimitiveInvocationMode.CatchUp) CompleteAdvance();
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            CompleteAfterPresentationFailure();
        }
        catch (Exception error)
        {
            GameLog.Logger.Warning(error, "NVL presentation failed");
            CompleteAfterPresentationFailure();
        }
    }

    private void CompleteAfterPresentationFailure()
    {
        lock (_gate)
            _phase = NvlAppendPhase.Completed;
        TryComplete();
    }

    private enum NvlAppendPhase
    {
        Created,
        Typing,
        WaitingAdvance,
        Completed
    }
}
