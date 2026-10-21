using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Settings;
using GalNet.Runtime.Logging;

namespace GalNet.Runtime.Engine;

public sealed partial class GameEngine
{
    private readonly HashSet<PrimitiveInstance> _autoBlockers = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<PlaybackMode> _playbackChanges = new();
    private PlaybackMode _playbackMode;
    private bool _advanceBoundary;
    private long _playbackRevision;
    private CancellationTokenSource? _playbackTimer;

    public PlaybackMode PlaybackMode => _playbackMode;
    public event Action<PlaybackMode>? PlaybackModeChanged;

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

    public async Task RefreshPlaybackSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancelPlaybackTimer();
            if (_playbackMode == PlaybackMode.Quick && IsQuickBlockedByUnreadText()) SetPlaybackModeCore(PlaybackMode.Manual);
            ReconcilePlaybackTimer();
        }
        finally { _dispatchGate.Release(); PublishStateChanges(); }
    }

    private bool IsQuickBlockedByUnreadText() => _runtime.Settings.Get<GameSettings>().QuickReadOnly &&
        _active.Any(item => item.IsReading && !item.WasRead);

    private void SetPlaybackModeCore(PlaybackMode mode)
    {
        if (_playbackMode == mode) return;
        CancelPlaybackTimer();
        _playbackMode = mode;
        lock (_notificationSync) _playbackChanges.Enqueue(mode);
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
            _runtime.Settings.Get<GameSettings>().QuickReadOnly && _progress?.IsRead(group.Id, readingId) != true)
            SetPlaybackModeCore(PlaybackMode.Manual);
    }

    private void ReconcileAutoBlocker(ActivePrimitive item)
    {
        if (item.Instance.IsAutoBlocking) _autoBlockers.Add(item.Instance);
        else _autoBlockers.Remove(item.Instance);
        if (!item.ReadMarked && item.IsReading && item.Instance is IAdvanceWaitInstance { IsTextPresented: true })
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

    private bool CanSchedulePlayback() => _playbackMode != PlaybackMode.Manual && _advanceBoundary && IsRunning &&
        _pendingChoice is null && !_disposed && Volatile.Read(ref _stopRequested) == 0 &&
        _autoBlockers.Count == 0 && _active.All(item => !item.Instance.IsBlocking || item.Instance.IsCompleted ||
            item.Instance is IAdvanceWaitInstance { IsWaitingForAdvance: true });

    private void ReconcilePlaybackTimer()
    {
        foreach (var item in _active) ReconcileAutoBlocker(item);
        if (!CanSchedulePlayback()) { CancelPlaybackTimer(); return; }
        if (_playbackTimer is not null) return;
        var settings = _runtime.Settings.Get<GameSettings>();
        var interval = _playbackMode == PlaybackMode.Auto ? settings.AutoAdvanceIntervalMilliseconds : settings.QuickAdvanceIntervalMilliseconds;
        if (interval < 0) throw new InvalidDataException("Playback intervals must be nonnegative milliseconds.");
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
                ContinueUntilBlocked();
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

    private void PublishPlaybackChanges()
    {
        while (true)
        {
            PlaybackMode mode;
            lock (_notificationSync) if (!_playbackChanges.TryDequeue(out mode)) return;
            foreach (var handler in PlaybackModeChanged?.GetInvocationList() ?? [])
                try { ((Action<PlaybackMode>)handler)(mode); }
                catch (Exception exception) { GameLog.Logger.Error(exception, "Playback observer failed"); }
        }
    }
}
