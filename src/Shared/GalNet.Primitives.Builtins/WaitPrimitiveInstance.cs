using GalNet.Core.Primitives;

namespace GalNet.Primitives.Builtins;

public sealed class WaitPrimitiveInstance : PrimitiveInstance
{
    private readonly object _gate = new();
    private readonly TimeSpan _duration;
    private readonly CancellationToken _scopeCancellation;
    private CancellationTokenSource? _delayCancellation;
    private bool _skippable = true;

    public WaitPrimitiveInstance(TimeSpan duration, string? batchId, CancellationToken scopeCancellation)
        : base(batchId)
    {
        _duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _scopeCancellation = scopeCancellation;
    }

    public override bool IsBlocking => true;
    public override bool IsSkippable
    {
        get
        {
            lock (_gate)
                return _skippable;
        }
    }

    protected override void OnDispatch()
    {
        if (_duration == TimeSpan.Zero)
        {
            Complete();
            return;
        }

        _delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(_scopeCancellation);
        _ = RunDelayAsync(_delayCancellation.Token);
    }

    protected override void OnSkip()
    {
        lock (_gate)
        {
            if (!_skippable || IsCompleted)
                return;
            _skippable = false;
        }

        _delayCancellation?.Cancel();
        Complete();
    }

    private async Task RunDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_duration, cancellationToken).ConfigureAwait(false);
            Complete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { }
    }

    private void Complete()
    {
        lock (_gate)
            _skippable = false;
        TryComplete();
        _delayCancellation?.Dispose();
        _delayCancellation = null;
    }
}
