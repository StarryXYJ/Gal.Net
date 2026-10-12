namespace GalNet.Core.Primitives;

/// <summary>Mutable state for one dispatched primitive call.</summary>
public abstract class PrimitiveInstance
{
    private readonly object _completionGate = new();
    private int _dispatchStarted;
    private int _completed;
    private int _autoBlocking = -1;

    protected PrimitiveInstance(string? batchId = null) => BatchId =
        string.IsNullOrWhiteSpace(batchId) ? null : batchId;

    public abstract bool IsBlocking { get; }
    public abstract bool IsSkippable { get; }
    public string? BatchId { get; }
    public bool IsCompleted => Volatile.Read(ref _completed) != 0;
    /// <summary>Finishes after asynchronous presentation and its cleanup, which may outlive logical completion.</summary>
    public virtual Task PresentationCompletion => Task.CompletedTask;
    public bool IsAutoBlocking => !IsCompleted && (Volatile.Read(ref _autoBlocking) switch
    {
        -1 => IsBlocking,
        0 => false,
        _ => true
    });
    public PrimitiveInvocationMode InvocationMode { get; private set; }
    public event Action<PrimitiveInstance>? AutoBlockingChanged;

    /// <summary>Raised exactly once after completion becomes visible.</summary>
    public event Action<PrimitiveInstance>? Completed;

    public void Dispatch() => Dispatch(PrimitiveInvocationMode.Normal);

    public void Dispatch(PrimitiveInvocationMode mode)
    {
        if (Interlocked.Exchange(ref _dispatchStarted, 1) != 0)
            throw new InvalidOperationException("A primitive instance can only be dispatched once.");

        try
        {
            InvocationMode = mode;
            switch (mode)
            {
                case PrimitiveInvocationMode.Quick: OnQuickDispatch(); break;
                case PrimitiveInvocationMode.CatchUp: OnCatchUpDispatch(); break;
                default: OnDispatch(); break;
            }
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
    protected virtual void OnQuickDispatch() { OnDispatch(); OnPlaybackModeChanged(PrimitiveInvocationMode.Quick); }
    protected virtual void OnCatchUpDispatch() { OnDispatch(); OnPlaybackModeChanged(PrimitiveInvocationMode.CatchUp); }
    protected virtual void OnPlaybackModeChanged(PrimitiveInvocationMode mode) { }

    public void ApplyPlaybackMode(PrimitiveInvocationMode mode)
    {
        if (IsCompleted || InvocationMode == mode) return;
        InvocationMode = mode;
        OnPlaybackModeChanged(mode);
    }

    protected void SetAutoBlocking(bool value)
    {
        bool changed;
        lock (_completionGate)
        {
            if (IsCompleted) return;
            changed = IsAutoBlocking != value;
            Volatile.Write(ref _autoBlocking, value ? 1 : 0);
        }
        if (changed) AutoBlockingChanged?.Invoke(this);
    }
    protected virtual void OnSkip() { }

    protected bool TryComplete()
    {
        lock (_completionGate)
        {
            if (_completed != 0)
                return false;
            Volatile.Write(ref _completed, 1);
        }

        AutoBlockingChanged?.Invoke(this);
        Completed?.Invoke(this);
        return true;
    }
}
