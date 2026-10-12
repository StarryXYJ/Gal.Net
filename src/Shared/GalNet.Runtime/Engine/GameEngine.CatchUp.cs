using GalNet.Core.Primitives;
using GalNet.Core.Runtime;

namespace GalNet.Runtime.Engine;

public sealed partial class GameEngine
{
    private readonly SemaphoreSlim _catchUpSignal = new(0);
    private CancellationTokenSource? _catchUpCancellation;
    private Task? _catchUpTask;
    private bool _catchUpActive;
    private int _catchUpBudget;
    private bool _catchUpStopAtBoundary;
    public bool IsCatchingUp => _catchUpActive;

    /// <summary>Runs the current path, including conditions and side effects, until unread text or a choice.</summary>
    public Task SkipToUnreadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_lifecycleSync)
        {
            if (_catchUpTask is { IsCompleted: false }) return _catchUpTask;
            if (Volatile.Read(ref _stopRequested) != 0) return Task.CompletedTask;
            _catchUpCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _scopeCancellation.Token);
            var cancellation = _catchUpCancellation;
            _catchUpTask = Task.Run(() => CatchUpCoreAsync(cancellation), CancellationToken.None);
            _backgroundTasks.Add(DrainCatchUpAsync(_catchUpTask));
            return _catchUpTask;
        }
    }

    public void CancelCatchUp()
    {
        lock (_lifecycleSync) _catchUpCancellation?.Cancel();
    }

    private static async Task DrainCatchUpAsync(Task operation)
    {
        // The caller receives the exception and CatchUpCore publishes Faulted. Stop still
        // needs a non-faulting drain task so a failed presentation cannot skip teardown.
        try { await operation.ConfigureAwait(false); }
        catch (Exception) { }
    }

    private async Task CancelCatchUpAndWaitAsync(CancellationToken cancellationToken)
    {
        Task? pending;
        lock (_lifecycleSync)
        {
            _catchUpCancellation?.Cancel();
            pending = _catchUpTask is { IsCompleted: false } ? _catchUpTask : null;
        }
        if (pending is not null) await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CatchUpCoreAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var beganPresentation = false;
        Exception? failure = null;
        IGameRuntime? scopedRuntime = null;
        try
        {
            await _dispatchGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                if (!IsRunning || _pendingChoice is not null || _active.Any(item => item.IsReading && !item.WasRead)) return;
                scopedRuntime = _dispatchRuntime;
                SetPlaybackModeCore(PlaybackMode.Manual);
                _advanceBoundary = false;
                _catchUpActive = true;
                _catchUpStopAtBoundary = false;
                if (_catchUpPresentation is not null)
                {
                    beganPresentation = true;
                    await _catchUpPresentation.BeginCatchUpAsync(token).ConfigureAwait(false);
                }
                var entering = _active.ToArray();
                foreach (var item in entering) item.Instance.ApplyPlaybackMode(PrimitiveInvocationMode.CatchUp);
                // Mode changes may complete logically before the old renderer finishes cleanup.
                // Drain before executing/rebuilding anything with the same playback handles.
                await Task.WhenAll(entering.Select(item => item.Instance.PresentationCompletion))
                    .WaitAsync(_scopeCancellation.Token).ConfigureAwait(false);
                RemoveCompleted();
            }
            finally { _dispatchGate.Release(); PublishStateChanges(); }

            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool waiting;
                await _dispatchGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!ReferenceEquals(scopedRuntime, _dispatchRuntime)) return;
                    RemoveCompleted();
                    if (!_catchUpActive || !IsRunning || _pendingChoice is not null) break;
                    _catchUpBudget = 128;
                    ContinueUntilBlocked();
                    ThrowFlowError();
                    if (!_catchUpActive || !IsRunning || _pendingChoice is not null) break;
                    waiting = HasBlockingInstance();
                }
                finally { CaptureStateChange(); _dispatchGate.Release(); PublishStateChanges(); }
                if (waiting) await _catchUpSignal.WaitAsync(token).ConfigureAwait(false);
                else await Task.Yield();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { failure = exception; }
        finally
        {
            await _dispatchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(scopedRuntime, _dispatchRuntime))
                {
                    _catchUpActive = false;
                    var resumeAtBoundary = _catchUpStopAtBoundary && !token.IsCancellationRequested;
                    _catchUpStopAtBoundary = false;
                    foreach (var item in _active.ToArray()) item.Instance.ApplyPlaybackMode(PrimitiveInvocationMode.Normal);
                    if (beganPresentation && _catchUpPresentation is not null && !_scopeCancellation.IsCancellationRequested)
                        await _catchUpPresentation.EndCatchUpAsync(scopedRuntime, _scopeCancellation.Token).ConfigureAwait(false);
                    if (resumeAtBoundary)
                    {
                        ContinueUntilBlocked();
                    }
                }
            }
            catch (Exception exception) { failure ??= exception; }
            finally
            {
                if (failure is not null && ReferenceEquals(scopedRuntime, _dispatchRuntime))
                {
                    _flowError = failure;
                    IsRunning = false;
                }
                CaptureStateChange(); _dispatchGate.Release(); PublishStateChanges();
            }
            lock (_lifecycleSync)
            {
                if (ReferenceEquals(_catchUpCancellation, cancellation)) _catchUpCancellation = null;
                cancellation.Dispose();
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
