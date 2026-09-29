namespace GalNet.Core.Primitives;

/// <summary>Mutable state for one dispatched primitive call.</summary>
public abstract class PrimitiveInstance
{
    private readonly object _completionGate = new();
    private int _dispatchStarted;
    private int _completed;

    protected PrimitiveInstance(string? batchId = null) => BatchId =
        string.IsNullOrWhiteSpace(batchId) ? null : batchId;

    public abstract bool IsBlocking { get; }
    public abstract bool IsSkippable { get; }
    public string? BatchId { get; }
    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    /// <summary>Raised exactly once after completion becomes visible.</summary>
    public event Action<PrimitiveInstance>? Completed;

    public void Dispatch()
    {
        if (Interlocked.Exchange(ref _dispatchStarted, 1) != 0)
            throw new InvalidOperationException("A primitive instance can only be dispatched once.");

        try
        {
            OnDispatch();
        }
        catch
        {
            TryComplete();
            throw;
        }
    }

    public void Skip()
    {
        if (!IsCompleted && IsSkippable)
            OnSkip();
    }

    protected abstract void OnDispatch();
    protected virtual void OnSkip() { }

    protected bool TryComplete()
    {
        lock (_completionGate)
        {
            if (_completed != 0)
                return false;
            Volatile.Write(ref _completed, 1);
        }

        Completed?.Invoke(this);
        return true;
    }
}
