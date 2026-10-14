namespace GalNet.Core.Primitives;

/// <summary>One asynchronous operation with a stable completion task and overridable business behavior.</summary>
public abstract class AsyncPrimitiveInstance(string? batchId = null, bool blocking = false,
    CancellationToken cancellationToken = default) : PrimitiveInstance(batchId)
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public sealed override Task PresentationCompletion => _completion.Task;
    public override bool IsBlocking => blocking;
    public override bool IsSkippable => false;
    protected CancellationToken CancellationToken => cancellationToken;

    protected sealed override void OnDispatch()
    {
        Start();
        _ = ObserveAsync();
    }

    private void Start()
    {
        try { OnStarting(); }
        catch (Exception error) { Finish(error); _ = _completion.Task.Exception; throw; }
    }

    protected void DispatchWithoutExecution()
    {
        Start();
        CompleteWithoutExecution();
    }

    /// <summary>Synchronous state changes; failures retain the synchronous dispatch error path.</summary>
    protected virtual void OnStarting() { }
    protected abstract Task ExecuteAsync(CancellationToken token);
    /// <summary>Return true only when the business policy handles the failure. Otherwise the task propagates it.</summary>
    protected virtual bool HandleExecutionFailure(Exception exception) => false;

    /// <summary>Finish a mode which intentionally performs no asynchronous operation.</summary>
    protected void CompleteWithoutExecution() => Finish(null);

    private async Task ObserveAsync()
    {
        Exception? failure = null;
        try { await ExecuteAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error)
        {
            try { if (!HandleExecutionFailure(error)) failure = error; }
            catch (Exception handlerError) { failure = new AggregateException(error, handlerError); }
        }
        Finish(failure);
    }

    private void Finish(Exception? failure)
    {
        // Engine completion observers must see an already published presentation failure.
        if (failure is OperationCanceledException cancelled)
            _completion.TrySetCanceled(cancelled.CancellationToken);
        else if (failure is not null) _completion.TrySetException(failure);
        else _completion.TrySetResult();
        TryComplete();
    }
}
