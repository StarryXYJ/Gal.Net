using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

/// <summary>Text presentation ends before the separate player advance completes the line.</summary>
public abstract class TextAdvancePrimitiveInstance(string? batchId, CancellationToken cancellationToken)
    : PrimitiveInstance(batchId), IAdvanceWaitInstance
{
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _presentation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TextPhase _phase;
    public sealed override Task PresentationCompletion => _presentation.Task;
    public override bool IsBlocking => true;
    public override bool IsSkippable { get { lock (_gate) return _phase is TextPhase.Typing or TextPhase.WaitingAdvance; } }
    public bool IsTextPresented { get; private set; }
    public bool IsWaitingForAdvance { get { lock (_gate) return _phase == TextPhase.WaitingAdvance; } }
    public void CompleteAdvance() { if (IsWaitingForAdvance) OnSkip(); }

    protected virtual void OnTextStarting() { }
    protected abstract Task PresentTextAsync(bool immediately, CancellationToken token);
    protected abstract void SkipText();
    protected abstract void RevealAllText();
    protected virtual void RecordCatchUpText() { }
    protected virtual void HandlePresentationFailure(Exception exception) { }

    protected sealed override void OnDispatch()
    {
        lock (_gate) _phase = TextPhase.Typing;
        try { OnTextStarting(); }
        catch (Exception exception)
        {
            _presentation.TrySetException(exception);
            _ = _presentation.Task.Exception;
            throw;
        }
        _ = ObserveAsync();
    }

    protected override void OnSkip()
    {
        var complete = false;
        lock (_gate)
        {
            if (_phase == TextPhase.Typing) SkipText();
            else if (_phase == TextPhase.WaitingAdvance) { _phase = TextPhase.Completed; complete = true; }
        }
        if (complete) TryComplete();
    }

    protected override void OnCatchUpDispatch()
    {
        try { RecordCatchUpText(); }
        catch (Exception exception) { _presentation.TrySetException(exception); _ = _presentation.Task.Exception; throw; }
        lock (_gate) _phase = TextPhase.Completed;
        _presentation.TrySetResult();
        TryComplete();
    }

    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
    {
        if (mode is PrimitiveInvocationMode.Quick or PrimitiveInvocationMode.CatchUp) RevealAllText();
        if (mode == PrimitiveInvocationMode.CatchUp && IsWaitingForAdvance) CompleteAdvance();
    }

    private async Task ObserveAsync()
    {
        try
        {
            await PresentTextAsync(InvocationMode == PrimitiveInvocationMode.Quick, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_phase == TextPhase.Typing) _phase = TextPhase.WaitingAdvance;
                IsTextPresented = true;
            }
            SetAutoBlocking(false);
            _presentation.TrySetResult();
            if (InvocationMode == PrimitiveInvocationMode.CatchUp) CompleteAdvance();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { CompleteAfterFailure(); }
        catch (Exception exception)
        {
            try { HandlePresentationFailure(exception); }
            catch (Exception handlerFailure) { _presentation.TrySetException(new AggregateException(exception, handlerFailure)); }
            finally { CompleteAfterFailure(); }
        }
    }

    private void CompleteAfterFailure()
    {
        lock (_gate) _phase = TextPhase.Completed;
        _presentation.TrySetResult();
        TryComplete();
    }

    private enum TextPhase { Created, Typing, WaitingAdvance, Completed }
}
