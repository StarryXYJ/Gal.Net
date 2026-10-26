using GalNet.Core.Entry;
using GalNet.Core.Execution;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Runtime.Logging;

namespace GalNet.Runtime.Engine;

public sealed partial class GameEngine
{
    private PlaybackMode _playbackMode;
    private PlaybackOptions _playbackOptions;
    private bool _advanceBoundary;
    private long _playbackRevision;
    private CancellationTokenSource? _playbackTimer;

    public PlaybackMode PlaybackMode => _playbackMode;
    public event Action<PlaybackMode>? PlaybackModeChanged { add => _notifications.PlaybackChanged += value; remove => _notifications.PlaybackChanged -= value; }

    /// <summary>Immediately invalidates a pending advance; the following mode request updates state.</summary>
    public void CancelPlayback()
    {
        Interlocked.Increment(ref _playbackRevision);
        CancelCatchUp();
    }

    public async Task SetPlaybackModeAsync(PlaybackMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ThrowIfDisposed();
        await CancelCatchUpAndWaitAsync(cancellationToken).ConfigureAwait(false);
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopRequested) != 0) return;
            ThrowFlowError();
            if (_debugger?.Paused == true) mode = PlaybackMode.Manual;
            if (!IsRunning || _pendingChoice is not null) mode = PlaybackMode.Manual;
            if (mode == PlaybackMode.Quick && IsQuickBlockedByUnreadText()) mode = PlaybackMode.Manual;
            SetPlaybackModeCore(mode);
            foreach (var item in _active.ToArray())
                item.Instance.ApplyPlaybackMode(mode == PlaybackMode.Quick
                    ? PrimitiveInvocationMode.Quick : PrimitiveInvocationMode.Normal);
            RemoveCompleted();
            ReconcilePlaybackTimer();
        }
        finally
        {
            _dispatchGate.Release();
            PublishStateChanges();
        }
    }

    public async Task UpdatePlaybackOptionsAsync(PlaybackOptions options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _playbackOptions = options;
            CancelPlaybackTimer();
            if (_playbackMode == PlaybackMode.Quick && IsQuickBlockedByUnreadText()) SetPlaybackModeCore(PlaybackMode.Manual);
            ReconcilePlaybackTimer();
        }
        finally { _dispatchGate.Release(); PublishStateChanges(); }
    }

    private bool IsQuickBlockedByUnreadText() => _playbackOptions.QuickReadOnly &&
        _active.Any(item => item.IsReading && !item.WasRead);

    private void SetPlaybackModeCore(PlaybackMode mode)
    {
        if (_playbackMode == mode) return;
        CancelPlaybackTimer();
        _playbackMode = mode;
        _notifications.Playback(mode);
    }

    private void CheckReadingBoundary(Group group, PrimitiveEntry entry)
    {
        var readingId = entry.ReadingBoundaryId;
        if (readingId is null && _view.TryGetEntry(entry.Type, out var definition) && definition!.IsReading)
            readingId = entry.StableId;
        if (readingId is not null && _catchUpActive && _catchUpTarget == CatchUpTarget.Unread && _progress?.IsRead(group.Id, readingId) != true)
        {
            _catchUpActive = false;
            _catchUpStopAtBoundary = true;
        }
        if (readingId is not null && _playbackMode == PlaybackMode.Quick &&
            _playbackOptions.QuickReadOnly && _progress?.IsRead(group.Id, readingId) != true)
            SetPlaybackModeCore(PlaybackMode.Manual);
    }

    private void ReconcileAutoBlocker(ActivePrimitive item)
    {
        if (_debugger?.SuppressReadProgress != true && !item.ReadMarked && item.IsReading && item.Instance is IAdvanceWaitInstance { IsTextPresented: true })
        {
            if (string.IsNullOrWhiteSpace(item.StableId))
                throw new InvalidDataException("Reading primitives require a stable compiled identity.");
            _progress?.MarkRead(item.GroupId, item.StableId);
            item.ReadMarked = true;
        }
    }

    private void OnAutoBlockingChanged(PrimitiveInstance instance) => StartBackground(async () =>
    {
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var active = _active.Find(item => ReferenceEquals(item.Instance, instance));
            if (active is null || _disposed || Volatile.Read(ref _stopRequested) != 0) return;
            ReconcileAutoBlocker(active);
            ReconcilePlaybackTimer();
        }
        catch (Exception exception) { _flowError = exception; IsRunning = false; }
        finally { CaptureStateChange(); _dispatchGate.Release(); PublishStateChanges(); }
    });

    private bool CanSchedulePlayback() => _debugger?.Paused != true && _playbackMode != PlaybackMode.Manual && _advanceBoundary && IsRunning &&
        _pendingChoice is null && !_disposed && Volatile.Read(ref _stopRequested) == 0 &&
        !_active.Any(item => item.Instance.IsAutoBlocking) && _active.All(item => !item.Instance.IsBlocking || item.Instance.IsCompleted ||
            item.Instance is IAdvanceWaitInstance { IsWaitingForAdvance: true });

    private void ReconcilePlaybackTimer()
    {
        foreach (var item in _active) ReconcileAutoBlocker(item);
        if (!CanSchedulePlayback()) { CancelPlaybackTimer(); return; }
        if (_playbackTimer is not null) return;
        var interval = _playbackMode == PlaybackMode.Auto ? _playbackOptions.AutoAdvanceIntervalMilliseconds : _playbackOptions.QuickAdvanceIntervalMilliseconds;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_scopeCancellation.Token);
        _playbackTimer = cancellation;
        var revision = Interlocked.Increment(ref _playbackRevision);
        StartBackground(() => RunPlaybackTimerAsync(TimeSpan.FromMilliseconds(interval), revision, cancellation));
    }

    private void CancelPlaybackTimer()
    {
        Interlocked.Increment(ref _playbackRevision);
        var timer = _playbackTimer;
        _playbackTimer = null;
        timer?.Cancel();
    }

    private async Task RunPlaybackTimerAsync(TimeSpan interval, long revision, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Yield();
            await Task.Delay(interval, cancellation.Token).ConfigureAwait(false);
            await _dispatchGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                if (revision != Volatile.Read(ref _playbackRevision) || !CanSchedulePlayback()) return;
                _playbackTimer = null;
                _advanceBoundary = false;
                foreach (var item in _active.ToArray())
                    if (item.Instance is IAdvanceWaitInstance wait) wait.CompleteAdvance();
                RemoveCompleted();
                UpdateLastStableSnapshot();
                await ContinueUntilBlockedAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception exception) { _flowError = exception; IsRunning = false; }
            finally { CaptureStateChange(); _dispatchGate.Release(); PublishStateChanges(); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            await _dispatchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_playbackTimer, cancellation)) _playbackTimer = null;
                cancellation.Dispose();
            }
            finally { _dispatchGate.Release(); }
        }
    }

}
