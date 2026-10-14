namespace GalNet.Presentation.Abstractions.Execution;

/// <summary>Owns running module operations. Completed tasks are observed and removed; failures survive draining.</summary>
public sealed class PrimitiveTaskScope : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly HashSet<Task> _running = [];
    private readonly List<Exception> _failures = [];
    private Task? _disposeTask;

    public PrimitiveTaskScope() => CancellationToken = _cancellation.Token;
    public CancellationToken CancellationToken { get; }
    public int RunningCount { get { lock (_gate) return _running.Count; } }
    public void ThrowIfDisposed()
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
    }

    public Task Run(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            _running.Add(completion.Task);
        }
        _ = ExecuteAsync(operation, completion);
        return completion.Task;
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> operation, TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            CancellationToken.ThrowIfCancellationRequested();
            await operation(CancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        lock (_gate)
        {
            if (failure is OperationCanceledException cancelled)
                completion.TrySetCanceled(cancelled.CancellationToken);
            else if (failure is not null)
            {
                _failures.Add(failure);
                completion.TrySetException(failure);
                _ = completion.Task.Exception;
            }
            else completion.TrySetResult();
            _running.Remove(completion.Task);
        }
    }

    public void Dispose() => _ = BeginDispose();
    public ValueTask DisposeAsync() => new(BeginDispose());

    private Task BeginDispose()
    {
        TaskCompletionSource completion;
        Task[] running;
        lock (_gate)
        {
            if (_disposeTask is not null) return _disposeTask;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            running = _running.ToArray();
        }
        _ = DrainAsync(running, completion);
        return completion.Task;
    }

    private async Task DrainAsync(Task[] running, TaskCompletionSource completion)
    {
        try { _cancellation.Cancel(); }
        catch (Exception error) { lock (_gate) _failures.Add(error); }
        try { await Task.WhenAll(running).ConfigureAwait(false); }
        catch { /* Failures were retained by ExecuteAsync; cancellation is expected while stopping. */ }
        finally { _cancellation.Dispose(); }
        lock (_gate)
        {
            if (_failures.Count == 0) completion.TrySetResult();
            else
            {
                completion.TrySetException(new AggregateException(_failures));
                _failures.Clear();
                _ = completion.Task.Exception;
            }
        }
    }
}
