using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Progress;
using GalNet.Runtime.Runtime;

namespace GalNet.Runtime.Engine;

/// <summary>Owns story flow and all active primitive instances for one game run.</summary>
public sealed partial class GameEngine : IDisposable, IAsyncDisposable, IPlayerPlaybackController
{
    private readonly GameGraphNavigator _navigation;
    private readonly IGameRuntime _runtime;
    private readonly IGameView _view;
    private readonly IChoicePresenter? _choicePresenter;
    private readonly IGameProgressService? _progress;
    private readonly ICatchUpPresentation? _catchUpPresentation;
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private CancellationTokenSource _scopeCancellation = new();
    private IGameRuntime _dispatchRuntime;
    private readonly ActivePrimitiveRegistry _active;
    private GameSnapshot _lastStableSnapshot;
    private PendingChoice? _pendingChoice;
    private Exception? _flowError;
    private readonly object _lifecycleSync = new();
    private readonly List<Task> _backgroundTasks = [];
    private readonly Queue<GameEngineStateChange> _stateChanges = new();
    private readonly object _notificationSync = new();
    private GameEngineState _reportedState = GameEngineState.Ready;
    private Task? _stopTask;
    private Task? _disposeTask;
    private int _stopRequested;
    private bool _started;
    private bool _stopped;
    private bool _publishingState;
    private volatile bool _disposed;

    public GameEngine(
        Graph graph,
        IGameView view,
        ITextResolver? textResolver = null,
        SettingsContainer? settings = null,
        IGameProgressService? progress = null,
        IChoicePresenter? choicePresenter = null,
        ICatchUpPresentation? catchUpPresentation = null)
        : this(
            graph,
            new GameRuntime(textResolver, graph?.RootNodeId ?? "", settings),
            view,
            progress,
            choicePresenter,
            catchUpPresentation)
    { }

    public GameEngine(
        Graph graph,
        IGameRuntime runtime,
        IGameView view,
        IGameProgressService? progress = null,
        IChoicePresenter? choicePresenter = null,
        ICatchUpPresentation? catchUpPresentation = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _navigation = new GameGraphNavigator(graph);
        _active = new ActivePrimitiveRegistry(OnBlockingCompleted, OnAutoBlockingChanged);
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _dispatchRuntime = new RunScopedRuntime(_runtime, _scopeCancellation.Token);
        var entries = graph.Nodes.OfType<Group>().SelectMany(group => group.Entries).ToArray();
        if (entries.Any(entry => entry is not PrimitiveEntry { IsGeneric: true }))
            throw new InvalidOperationException("The game graph must contain compiled generic primitives, not authoring entries.");
        var missing = entries.OfType<PrimitiveEntry>()
            .Where(entry => !_view.CanExecute(entry.Type)).Select(entry => entry.Type).Distinct().ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Game instructions have no implementation: {string.Join(", ", missing)}.");
        _progress = progress;
        _choicePresenter = choicePresenter;
        _catchUpPresentation = catchUpPresentation;
        _lastStableSnapshot = _runtime.CreateSnapshot();
    }

    public event Action<GameSnapshot>? CheckpointCreated;
    /// <summary>Published outside the execution lock. Handlers must not synchronously wait for engine tasks.</summary>
    public event Action<GameEngineStateChange>? StateChanged;
    public IGameRuntime Runtime => _runtime;
    public GameSnapshot LastStableSnapshot => SnapshotCopy.Clone(_lastStableSnapshot);
    public string CurrentNodeId => _runtime.CurrentNodeId;
    public int EntryIndex => _runtime.EntryIndex;
    public bool IsRunning { get; private set; }
    public GameEngineState State => _stopped ? GameEngineState.Stopped
        : Volatile.Read(ref _stopRequested) != 0 ? _reportedState
        : _flowError is not null ? GameEngineState.Faulted
        : !_started ? GameEngineState.Ready
        : IsRunning ? GameEngineState.Running : GameEngineState.Completed;

    /// <summary>Applies one player advance and runs until the next blocking boundary.</summary>
    public async Task<bool> AdvanceAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await CancelCatchUpAndWaitAsync(cancellationToken).ConfigureAwait(false);
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopRequested) != 0) return false;
            ThrowIfDisposed();
            ThrowFlowError();
            if (_started && !IsRunning) return false;
            _started = true;
            _catchUpActive = false;
            _catchUpStopAtBoundary = false;
            SetPlaybackModeCore(PlaybackMode.Manual);
            IsRunning = !_runtime.IsGameEnded;
            CaptureStateChange();
            RemoveCompleted();
            UpdateLastStableSnapshot();

            if (_pendingChoice is not null || !IsRunning)
                return IsRunning;

            var blocker = _active.EarliestBlocker();
            if (blocker is not null)
            {
                foreach (var candidate in _active.SkipCandidates(blocker).ToArray())
                    candidate.Instance.Skip();

                RemoveCompleted();
                UpdateLastStableSnapshot();
                if (_active.HasBlockingInstance())
                    return true;
            }

            // A manual skip may only reveal text or seek a video segment. Keep the
            // existing boundary until its blocker is actually released.
            _advanceBoundary = false;
            ContinueUntilBlocked();
            ThrowFlowError();
            return IsRunning;
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _flowError ??= exception;
            IsRunning = false;
            throw;
        }
        finally
        {
            CaptureStateChange();
            _dispatchGate.Release();
            PublishStateChanges();
        }
    }

    public GameSnapshot CreateSaveData() => SnapshotCopy.Clone(_lastStableSnapshot);

    /// <summary>
    /// Rebuilds host presentation under the execution gate using this run's isolated scene and token.
    /// The caller token cancels gate entry; retained playback uses the run token until stop/restore.
    /// </summary>
    public async Task SynchronizePresentationAsync(Func<IGameRuntime, CancellationToken, Task> synchronize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synchronize);
        ThrowIfDisposed();
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _stopRequested) != 0) throw new InvalidOperationException("A stopped engine cannot synchronize presentation.");
            await synchronize(_dispatchRuntime, _scopeCancellation.Token).ConfigureAwait(false);
        }
        finally { _dispatchGate.Release(); }
    }

    public void RestoreFrom(GameSnapshot data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ThrowIfDisposed();
        if (data.Version != GameSnapshot.CurrentFormatVersion)
            throw new InvalidDataException($"Unsupported save version '{data.Version}'.");
        data = SnapshotCopy.Clone(data);
        _dispatchGate.Wait();
        try
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _stopRequested) != 0)
                throw new InvalidOperationException("A stopped engine cannot be restored. Create a new engine.");
            CancelPendingChoice();
            CancelCatchUp();
            _catchUpActive = false;
            _catchUpStopAtBoundary = false;
            SetPlaybackModeCore(PlaybackMode.Manual);
            _advanceBoundary = false;
            ClearActive();
            var previousScope = _scopeCancellation;
            previousScope.Cancel();
            previousScope.Dispose();
            _runtime.RestoreFrom(data);
            _scopeCancellation = new CancellationTokenSource();
            _dispatchRuntime = new RunScopedRuntime(_runtime, _scopeCancellation.Token);
            _lastStableSnapshot = _runtime.CreateSnapshot();
            _active.EndGroup();
            _flowError = null;
            IsRunning = true;
            _started = false;
        }
        finally
        {
            CaptureStateChange();
            _dispatchGate.Release();
            PublishStateChanges();
        }
    }

    /// <summary>Terminal stop: cancel execution and drain internal continuations without owning the view.</summary>
    public Task StopAsync()
    {
        CancelCatchUp();
        lock (_lifecycleSync)
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            return _stopTask ??= Task.Run(StopCoreAsync);
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleSync)
        {
            _disposed = true;
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        finally { _scopeCancellation.Dispose(); }
    }

    private async Task StopCoreAsync()
    {
        Exception? cancellationError = null;
        var scope = _scopeCancellation;
        try { scope.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception exception) { cancellationError = exception; }
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Restore may have replaced the scope while stop was waiting for the gate.
            try
            {
                if (!ReferenceEquals(scope, _scopeCancellation)) _scopeCancellation.Cancel();
                CancelPendingChoice();
            }
            catch (Exception exception) { cancellationError = exception; }
            ClearActive();
            SetPlaybackModeCore(PlaybackMode.Manual);
            _catchUpActive = false;
            _catchUpStopAtBoundary = false;
            _advanceBoundary = false;
            IsRunning = false;
        }
        finally
        {
            _dispatchGate.Release();
        }
        Task[] background;
        lock (_lifecycleSync) background = _backgroundTasks.ToArray();
        await Task.WhenAll(background).ConfigureAwait(false);
        Exception? progressError = null;
        try { if (_progress is not null) await _progress.FlushAsync().ConfigureAwait(false); }
        catch (Exception exception) { progressError = exception; }
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _stopped = true;
            CaptureStateChange();
        }
        finally { _dispatchGate.Release(); }
        PublishStateChanges();
        var errors = new[] { cancellationError, progressError }.OfType<Exception>().ToArray();
        if (errors.Length > 0) throw new AggregateException("Game shutdown failed.", errors);
    }

    private void ContinueUntilBlocked()
    {
        while (IsRunning && _pendingChoice is null && !_active.HasBlockingInstance() && !_catchUpStopAtBoundary)
        {
            if (_catchUpActive && --_catchUpBudget < 0) break;
            _scopeCancellation.Token.ThrowIfCancellationRequested();
            if (_runtime.IsGameEnded)
            {
                IsRunning = false;
                break;
            }

            var node = _navigation.FindNode(_runtime.CurrentNodeId);
            if (node is null)
            {
                IsRunning = false;
                break;
            }

            switch (node)
            {
                case Group group:
                    ProcessGroup(group);
                    break;
                case Branch { BranchType: BranchType.Choice } choice:
                    ProcessChoice(choice);
                    break;
                case Branch branch:
                    ProcessConditionBranch(branch);
                    UpdateLastStableSnapshot();
                    break;
                default:
                    IsRunning = false;
                    break;
            }
        }
        ReconcilePlaybackTimer();
    }

    private void ProcessGroup(Group group)
    {
        _active.BeginGroupExecution(group.Id);
        while (_runtime.EntryIndex < group.Entries.Count && !_active.HasBlockingInstance())
        {
            if (_catchUpActive && --_catchUpBudget < 0) return;
            _scopeCancellation.Token.ThrowIfCancellationRequested();
            var entry = group.Entries[_runtime.EntryIndex];
            PrimitiveInstance? instance = null;
            if (_runtime.EvaluateCondition(entry.Condition) && entry is PrimitiveEntry primitive)
            {
                CheckReadingBoundary(group, primitive);
                if (_catchUpStopAtBoundary) return;
                var wasRead = _progress?.IsRead(group.Id, primitive.StableId) == true;
                instance = Dispatch(group, primitive);
                Track(instance, group.Id, primitive, wasRead);
            }
            else if (entry is not PrimitiveEntry)
                throw new InvalidOperationException($"Runtime cannot execute authoring entry '{entry.Type}'.");

            _runtime.SetEntryIndex(_runtime.EntryIndex + 1);
            RemoveCompleted();
            UpdateLastStableSnapshot();
        }

        if (_runtime.EntryIndex < group.Entries.Count || _active.HasBlockingInstance())
            return;

        _runtime.SetEntryIndex(0);
        _active.EndGroup();
        MoveToNext();
        RemoveCompleted();
        UpdateLastStableSnapshot();
    }

    private PrimitiveInstance Dispatch(Group group, PrimitiveEntry entry)
    {
        if (!entry.IsGeneric)
            throw new InvalidOperationException($"Runtime cannot execute authoring entry '{entry.Type}'.");

        var instance = _view.Dispatch(entry, _dispatchRuntime, _scopeCancellation.Token,
            _catchUpActive ? PrimitiveInvocationMode.CatchUp :
            _playbackMode == PlaybackMode.Quick ? PrimitiveInvocationMode.Quick : PrimitiveInvocationMode.Normal);

        if (instance is null)
        {
            throw new InvalidOperationException($"Instruction '{entry.Type}' returned no instance.");
        }

        return instance;
    }

    private void Track(PrimitiveInstance instance, string groupId, PrimitiveEntry entry, bool wasRead)
    {
        var active = _active.Track(instance, groupId, entry.StableId, wasRead,
            _view.TryGetEntry(entry.Type, out var definition) && definition!.IsReading);
        if (instance.IsBlocking && !instance.IsCompleted) _advanceBoundary = true;
        ReconcileAutoBlocker(active);
    }

    private void ProcessConditionBranch(Branch branch)
    {
        for (var index = 0; index < branch.Conditions.Count; index++)
        {
            if (!_runtime.EvaluateCondition(branch.Conditions[index].Expression))
                continue;
            JumpFrom(branch.Id, index);
            return;
        }
        MoveToNext();
    }

    private void ProcessChoice(Branch branch)
    {
        if (_catchUpActive)
        {
            _catchUpActive = false;
            _catchUpStopAtBoundary = true;
            return;
        }
        SetPlaybackModeCore(PlaybackMode.Manual);
        _catchUpActive = false;
        _advanceBoundary = false;
        var visible = branch.Options
            .Select((option, outlet) => new VisibleChoice(
                outlet,
                _runtime.TextResolver.Resolve(option.Text),
                option.Condition))
            .Where(option => _runtime.EvaluateCondition(option.Condition))
            .ToArray();
        if (visible.Length == 0)
        {
            MoveToNext();
            UpdateLastStableSnapshot();
            return;
        }
        if (_choicePresenter is null)
            throw new InvalidOperationException("A choice presenter is required to execute a Choice branch.");

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_scopeCancellation.Token);
        var pending = new PendingChoice(branch.Id, visible, cancellation);
        _pendingChoice = pending;

        Task<int> selection;
        try
        {
            selection = _choicePresenter.ChooseAsync(
                visible.Select(option => option.Text).ToArray(),
                cancellation.Token);
        }
        catch
        {
            _pendingChoice = null;
            cancellation.Dispose();
            throw;
        }
        StartBackground(() => CompleteChoiceAsync(pending, selection));
    }

    private async Task CompleteChoiceAsync(PendingChoice pending, Task<int> selection)
    {
        int selected;
        try
        {
            selected = await selection.WaitAsync(pending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pending.Cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            await RecordFlowErrorAsync(pending, exception).ConfigureAwait(false);
            return;
        }

        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_pendingChoice, pending) || _disposed || Volatile.Read(ref _stopRequested) != 0)
                return;
            _pendingChoice = null;
            pending.Cancellation.Dispose();
            if (selected < 0 || selected >= pending.Options.Count)
            {
                _flowError = new InvalidOperationException(
                    $"Choice presenter returned invalid visible index {selected}.");
                IsRunning = false;
                return;
            }

            JumpFrom(pending.BranchId, pending.Options[selected].Outlet);
            UpdateLastStableSnapshot();
            ContinueUntilBlocked();
        }
        catch (Exception exception)
        {
            _flowError = exception;
            IsRunning = false;
        }
        finally
        {
            CaptureStateChange();
            _dispatchGate.Release();
            PublishStateChanges();
        }
    }

    private async Task RecordFlowErrorAsync(PendingChoice pending, Exception exception)
    {
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_pendingChoice, pending) || _disposed || Volatile.Read(ref _stopRequested) != 0)
                return;
            _pendingChoice = null;
            pending.Cancellation.Dispose();
            _flowError = exception;
            IsRunning = false;
        }
        finally
        {
            CaptureStateChange();
            _dispatchGate.Release();
            PublishStateChanges();
        }
    }

    private void OnBlockingCompleted(PrimitiveInstance instance) =>
        StartBackground(() => ContinueAfterBlockingCompletionAsync(instance));

    private async Task ContinueAfterBlockingCompletionAsync(PrimitiveInstance instance)
    {
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || Volatile.Read(ref _stopRequested) != 0 || !_active.Any(item => ReferenceEquals(item.Instance, instance)))
                return;
            var wasBlocking = instance.IsBlocking;
            RemoveCompleted();
            UpdateLastStableSnapshot();
            if (_pendingChoice is null && !_active.HasBlockingInstance() && wasBlocking && _playbackMode == PlaybackMode.Manual && !_catchUpActive)
            {
                _advanceBoundary = false;
                ContinueUntilBlocked();
            }
            ReconcilePlaybackTimer();
            _catchUpSignal.Release();
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested)
        { }
        catch (Exception exception)
        {
            _flowError = exception;
            IsRunning = false;
        }
        finally
        {
            CaptureStateChange();
            _dispatchGate.Release();
            PublishStateChanges();
        }
    }

    private void StartBackground(Func<Task> work)
    {
        lock (_lifecycleSync)
        {
            if (_disposed || Volatile.Read(ref _stopRequested) != 0) return;
            _backgroundTasks.RemoveAll(task => task.IsCompleted);
            _backgroundTasks.Add(Task.Run(work));
        }
    }

    // Called under the execution lock; delivery is deferred until after it is released.
    private void CaptureStateChange()
    {
        if (_flowError is not null || (_started && !IsRunning)) SetPlaybackModeCore(PlaybackMode.Manual);
        if (Volatile.Read(ref _stopRequested) != 0 && !_stopped) return;
        var state = State;
        if (state == _reportedState) return;
        _reportedState = state;
        lock (_notificationSync)
            _stateChanges.Enqueue(new GameEngineStateChange(state, state == GameEngineState.Faulted ? _flowError : null));
    }

    private void PublishStateChanges()
    {
        PublishPlaybackChanges();
        lock (_notificationSync)
        {
            if (_publishingState) return;
            _publishingState = true;
        }
        while (true)
        {
            GameEngineStateChange change;
            lock (_notificationSync)
            {
                if (!_stateChanges.TryDequeue(out change!))
                {
                    _publishingState = false;
                    return;
                }
            }
            foreach (var handler in StateChanged?.GetInvocationList() ?? [])
                try { ((Action<GameEngineStateChange>)handler)(change); }
                catch (Exception exception) { GameLog.Logger.Error(exception, "Game state observer failed"); }
        }
    }

    private void RemoveCompleted() => _active.RemoveCompleted(ReconcileAutoBlocker, instance => _autoBlockers.Remove(instance));

    private void ClearActive()
    {
        _active.Clear();
        _autoBlockers.Clear();
        CancelPlaybackTimer();
    }

    private void JumpFrom(string nodeId, int outlet)
    {
        var target = _navigation.FindTarget(nodeId, outlet);
        if (target is null) IsRunning = false;
        else _runtime.JumpTo(target);
    }

    private void MoveToNext() => JumpFrom(_runtime.CurrentNodeId, 0);

    private void UpdateLastStableSnapshot()
    {
        if (_active.HasBlockingInstance() || _pendingChoice is not null)
            return;
        _lastStableSnapshot = _runtime.CreateSnapshot();
        CheckpointCreated?.Invoke(SnapshotCopy.Clone(_lastStableSnapshot));
    }

    private void CancelPendingChoice()
    {
        var pending = _pendingChoice;
        _pendingChoice = null;
        if (pending is null)
            return;
        try { pending.Cancellation.Cancel(); }
        finally { pending.Cancellation.Dispose(); }
    }

    private void ThrowFlowError()
    {
        if (_flowError is { } exception)
            throw new InvalidOperationException("The game flow failed.", exception);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record VisibleChoice(int Outlet, string Text, string Condition);

    private sealed record PendingChoice(
        string BranchId,
        IReadOnlyList<VisibleChoice> Options,
        CancellationTokenSource Cancellation)
    {
        public CancellationToken Token { get; } = Cancellation.Token;
    }
}
