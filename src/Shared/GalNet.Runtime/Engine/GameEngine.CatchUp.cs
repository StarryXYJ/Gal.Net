using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;

namespace GalNet.Runtime.Engine;

public sealed partial class GameEngine
{
    private enum CatchUpTarget { Unread, GroupEnd, DebugPosition }

    private readonly SemaphoreSlim _catchUpSignal = new(0);
    private CancellationTokenSource? _catchUpCancellation;
    private Task? _catchUpTask;
    private bool _catchUpActive;
    private int _catchUpBudget;
    private bool _catchUpStopAtBoundary;
    private CatchUpTarget _catchUpTarget;
    private bool _catchUpIgnoreConditions;
    public bool IsCatchingUp => _catchUpActive;

    /// <summary>Runs the current path, including conditions and side effects, until unread text or a choice.</summary>
    public Task SkipToUnreadAsync(CancellationToken cancellationToken = default) =>
        StartCatchUp(CatchUpTarget.Unread, false, cancellationToken);

    /// <summary>
    /// Catches up through the current group's remaining entries, including unread text, without entering its successor.
    /// Conditions are evaluated unless explicitly ignored; variable assignments and other logical effects still execute.
    /// The next advance leaves the group. Does nothing without a running group.
    /// </summary>
    public Task SkipToGroupEndAsync(bool ignoreConditions = false, CancellationToken cancellationToken = default) =>
        StartCatchUp(CatchUpTarget.GroupEnd, ignoreConditions, cancellationToken);

    private Task StartCatchUp(CatchUpTarget target, bool ignoreConditions, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        lock (_lifecycleSync)
        {
            if (_catchUpTask is { IsCompleted: false })
            {
                if (_catchUpTarget != target || _catchUpIgnoreConditions != ignoreConditions)
                    throw new InvalidOperationException("A different catch-up operation is already in progress.");
                return _catchUpTask;
            }
            if (Volatile.Read(ref _stopRequested) != 0) return Task.CompletedTask;
            _catchUpTarget = target;
            _catchUpIgnoreConditions = ignoreConditions;
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
                if (!IsRunning || _restorationPending || _pendingChoice is not null) return;
                if (_debugger?.Paused == true) throw new InvalidOperationException("Resume the debugger before starting player catch-up.");
                if (_catchUpTarget == CatchUpTarget.GroupEnd && _navigation.FindNode(_runtime.CurrentNodeId) is not Group) return;
                if (_catchUpTarget == CatchUpTarget.Unread && _active.Any(item => item.IsReading && !item.WasRead)) return;
                scopedRuntime = _dispatchRuntime;
                SetPlaybackModeCore(PlaybackMode.Manual);
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
                // An extension may retain its normal blocker during catch-up. If
                // catch-up is cancelled here, playback must still own that boundary.
                _advanceBoundary = _active.HasBlockingInstance();
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
                    waiting = _active.HasBlockingInstance();
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
                    var resumeAtBoundary = _catchUpStopAtBoundary && _catchUpTarget == CatchUpTarget.Unread && !token.IsCancellationRequested;
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
