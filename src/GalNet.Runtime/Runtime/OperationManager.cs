using GalNet.Core.Primitives;

namespace GalNet.Runtime.Runtime;

/// <summary>One accepted asynchronous primitive invocation owned by a Game Scope.</summary>
public sealed record PrimitiveOperation(
    Guid ExecutionId,
    long Sequence,
    string TypeId,
    string BatchKey,
    bool Blocking,
    bool Skippable,
    Task Completion,
    PrimitiveExecutionControl Control);

/// <summary>Tracks active primitive work and centralizes cross-module skip batches.</summary>
public sealed class OperationManager
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, PrimitiveOperation> _operations = [];
    private long _nextSequence;

    public IReadOnlyCollection<PrimitiveOperation> ActiveOperations
    {
        get
        {
            lock (_gate)
                return _operations.Values.OrderBy(operation => operation.Sequence).ToArray();
        }
    }

    public PrimitiveOperation? Track(string typeId, PrimitiveDispatch dispatch, PrimitiveExecutionControl control)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(control);

        if (dispatch.Status != PrimitiveDispatchStatus.Accepted || dispatch.Completion.IsCompleted)
            return null;

        PrimitiveOperation operation;
        lock (_gate)
        {
            var executionId = Guid.NewGuid();
            operation = new PrimitiveOperation(
                executionId,
                ++_nextSequence,
                typeId,
                dispatch.Policy.BatchId ?? executionId.ToString("N"),
                dispatch.Policy.Blocking,
                dispatch.Policy.Skippable,
                dispatch.Completion,
                control);
            _operations.Add(executionId, operation);
        }

        _ = RemoveWhenCompleteAsync(operation);
        return operation;
    }

    public async Task<bool> SkipNextBatchAsync()
    {
        PrimitiveOperation[] batch;
        lock (_gate)
        {
            var candidate = _operations.Values
                .Where(operation => operation.Skippable)
                .OrderBy(operation => operation.Sequence)
                .FirstOrDefault();
            if (candidate is null) return false;

            batch = _operations.Values
                .Where(operation => operation.Skippable && operation.BatchKey == candidate.BatchKey)
                .ToArray();
        }

        foreach (var operation in batch)
            operation.Control.RequestSkip();

        await Task.WhenAll(batch.Select(operation => operation.Completion));
        return true;
    }

    private async Task RemoveWhenCompleteAsync(PrimitiveOperation operation)
    {
        try
        {
            await operation.Completion.ConfigureAwait(false);
        }
        catch
        {
            // The Engine reports expected primitive failures; observing unexpected failures here
            // prevents background tasks from becoming unobserved while preserving progression.
        }
        finally
        {
            lock (_gate)
                _operations.Remove(operation.ExecutionId);
        }
    }
}
