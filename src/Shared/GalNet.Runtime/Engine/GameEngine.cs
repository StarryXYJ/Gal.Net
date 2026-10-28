using GalNet.Core.Entry;
using GalNet.Core.Execution;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Progress;
using GalNet.Runtime.Runtime;

namespace GalNet.Runtime.Engine;

/// <summary>Owns story flow and all active primitive instances for one game run.</summary>
public sealed partial class GameEngine : IDisposable, IAsyncDisposable, IPlayerPlaybackController
{
    private readonly GameGraphNavigator _navigation;
    private readonly IGameRuntime _runtime;
    private readonly IPrimitiveDispatcher _view;
    private readonly IChoicePresenter? _choicePresenter;
    private readonly IGameProgressService? _progress;
    private readonly ICatchUpPresentation? _catchUpPresentation;
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private CancellationTokenSource _scopeCancellation = new();
    private IGameContext _dispatchRuntime;
    private readonly ActivePrimitiveRegistry _active;
    private GameSnapshot _lastStableSnapshot;
    private PendingChoice? _pendingChoice;
    private Exception? _flowError;
    private readonly object _lifecycleSync = new();
    private readonly List<Task> _backgroundTasks = [];
    private readonly EngineNotifications _notifications = new();
    private GameEngineState _reportedState = GameEngineState.Ready;
    private Task? _stopTask;
    private Task? _disposeTask;
    private int _stopRequested;
    private bool _started;
    private bool _restorationPending;
    private bool _stopped;
    private volatile bool _disposed;

    public GameEngine(
        Graph graph,
        IPrimitiveDispatcher view,
        ITextResolver? textResolver = null,
        PlaybackOptions? playbackOptions = null,
        IGameProgressService? progress = null,
        IChoicePresenter? choicePresenter = null,
        ICatchUpPresentation? catchUpPresentation = null)
        : this(
            graph,
            new GameRuntime(textResolver, graph?.RootNodeId ?? "", saveDefaults: graph?.SaveVariables, playerDefaults: graph?.PlayerVariables),
            view,
            progress,
            choicePresenter,
            catchUpPresentation,
            playbackOptions)
    { }

    public GameEngine(
        Graph graph,
        IGameRuntime runtime,
        IPrimitiveDispatcher view,
        IGameProgressService? progress = null,
        IChoicePresenter? choicePresenter = null,
        ICatchUpPresentation? catchUpPresentation = null,
        PlaybackOptions? playbackOptions = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _playbackOptions = playbackOptions ?? new PlaybackOptions();
        _playbackOptions.Validate();
        _navigation = new GameGraphNavigator(graph);
        _active = new ActivePrimitiveRegistry(OnBlockingCompleted, OnAutoBlockingChanged);
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _runtime.State.Configure(_view.StateCatalog);
        _dispatchRuntime = new RunScopedRuntime(_runtime, _scopeCancellation.Token);
        var entries = _navigation.Nodes.OfType<Group>().SelectMany(group => group.Entries).ToArray();
        var missing = entries.OfType<PrimitiveEntry>()
            .Where(entry => !_view.CanExecute(entry.Type)).Select(entry => entry.Type).Distinct().ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Game instructions have no implementation: {string.Join(", ", missing)}.");
        _progress = progress;
        _choicePresenter = choicePresenter;
        _catchUpPresentation = catchUpPresentation;
        _lastStableSnapshot = _runtime.CreateSnapshot();
    }

    public event Action<GameSnapshot>? CheckpointCreated { add => _notifications.CheckpointCreated += value; remove => _notifications.CheckpointCreated -= value; }
    /// <summary>Published outside the execution lock. Handlers must not synchronously wait for engine tasks.</summary>
    public event Action<GameEngineStateChange>? StateChanged { add => _notifications.StateChanged += value; remove => _notifications.StateChanged -= value; }
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
            if (_debugger?.Paused == true) return IsRunning;
            if (_restorationPending) return IsRunning;
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
            await ContinueUntilBlockedAsync(cancellationToken).ConfigureAwait(false);
            ThrowFlowError();
            return IsRunning;
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _flowError ??= exception;
            IsRunning = false;
            throw;
        }
        finally
        {
            FinishDispatch();
        }
    }

    public GameSnapshot CreateSaveData() => SnapshotCopy.Clone(_lastStableSnapshot);

    /// <summary>
    /// Rebuilds host presentation under the execution gate using this run's isolated scene and token.
    /// The caller token cancels gate entry; retained playback uses the run token until stop/restore.
    /// </summary>
    public async Task SynchronizePresentationAsync(Func<IGameContext, CancellationToken, Task> synchronize,
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
            _restorationPending = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (!_scopeCancellation.IsCancellationRequested)
        {
            _flowError = error;
            IsRunning = false;
            throw;
        }
        finally { FinishDispatch(); }
    }

    public Task RestoreFromAsync(GameSnapshot data, CancellationToken cancellationToken = default) =>
        RestoreFromCoreAsync(data, cancellationToken: cancellationToken);

    /// <summary>Checks a replacement save against this game's contracts without changing the active run.</summary>
    public async Task ValidateRestoreAsync(GameSnapshot data, CancellationToken cancellationToken = default)
    {
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { ThrowIfDisposed(); _ = PrepareRestore(data); }
        finally { _dispatchGate.Release(); }
    }

    private GameRestorePoint PrepareRestore(GameSnapshot data)
    {
        var candidate = _runtime.PrepareRestore(data);
        var node = _navigation.FindNode(candidate.NodeId);
        if (node is null || candidate.EntryIndex > (node is Group group ? group.Entries.Count : 0))
            throw new InvalidDataException("The save position does not exist in this game graph.");
        _view.ValidateState(_runtime, candidate.State);
        return candidate;
    }

    private async Task<long> RestoreFromCoreAsync(GameSnapshot data, DebugSession? restoringDebugger = null,
        long? expectedGeneration = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ThrowIfDisposed();
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _stopRequested) != 0)
                throw new InvalidOperationException("A stopped engine cannot be restored. Create a new engine.");
            if (restoringDebugger is not null && (!ReferenceEquals(_debugger, restoringDebugger) || restoringDebugger.Generation != expectedGeneration))
                throw new InvalidOperationException("The execution changed before debug restoration.");
            var candidate = PrepareRestore(data);
            cancellationToken.ThrowIfCancellationRequested();
            CancelPendingChoice();
            CancelCatchUp();
            _catchUpActive = false;
            _catchUpStopAtBoundary = false;
            SetPlaybackModeCore(PlaybackMode.Manual);
            _advanceBoundary = false;
            ClearActive();
            var previousScope = _scopeCancellation;
            CancelDetachedScope(previousScope);
            _runtime.Restore(candidate);
            _debugger?.OnExternalRestore(restoringDebugger is not null);
            _restorationPending = _view.RequiresStateRestore;
            _scopeCancellation = new CancellationTokenSource();
            _dispatchRuntime = new RunScopedRuntime(_runtime, _scopeCancellation.Token);
            _lastStableSnapshot = _runtime.CreateSnapshot();
            _active.EndGroup();
            _flowError = null;
            IsRunning = true;
            _started = false;
            return _debugger?.Generation ?? 0;
        }
        finally
        {
            FinishDispatch();
        }
    }

    /// <summary>Terminal stop: cancel execution and drain internal continuations without owning the view.</summary>
    public Task StopAsync()
    {
        _debugger?.CancelSeek();
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
        if (_debugger is { } debugger) await debugger.PendingSeek.ConfigureAwait(false);
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

    // Called with the execution gate held; every exit (including cancellation) retains it.
    private async Task ContinueUntilBlockedAsync(CancellationToken cancellationToken = default)
    {
        var scope = _dispatchRuntime;
        var budget = 128;
        while (IsRunning && _debugger?.Paused != true && _pendingChoice is null && !_active.HasBlockingInstance() && !_catchUpStopAtBoundary)
        {
            if (_catchUpActive && --_catchUpBudget < 0) break;
            cancellationToken.ThrowIfCancellationRequested();
            if (--budget < 0)
            {
                CaptureStateChange();
                _dispatchGate.Release();
                try { PublishStateChanges(); await Task.Yield(); }
                finally { await _dispatchGate.WaitAsync(CancellationToken.None).ConfigureAwait(false); }
                if (!ReferenceEquals(scope, _dispatchRuntime) || Volatile.Read(ref _stopRequested) != 0) return;
                budget = 128;
                continue;
            }
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
                    ProcessGroup(group, ref budget, cancellationToken);
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

    private void ProcessGroup(Group group, ref int budget, CancellationToken cancellationToken)
    {
        if (_active.BeginGroupExecution(group.Id)) _debugger?.CaptureEntrance(group.Id);
        while (_runtime.EntryIndex < group.Entries.Count && _debugger?.Paused != true && !_active.HasBlockingInstance())
        {
            if (_catchUpActive && --_catchUpBudget < 0) return;
            if (--budget < 0) return;
            cancellationToken.ThrowIfCancellationRequested();
            _scopeCancellation.Token.ThrowIfCancellationRequested();
            var entry = group.Entries[_runtime.EntryIndex];
            if (_debugger?.BeforeEntry(group.Id, _runtime.EntryIndex) == true) return;
            if (_debugger?.Replaying == true) _debugger.RecordReplayInstruction();
            if (_debugger?.Replaying == true && _debugger.IgnoreInstruction(entry.Type))
            {
                _runtime.SetEntryIndex(_runtime.EntryIndex + 1);
                continue;
            }
            PrimitiveInstance? instance = null;
            if (((_catchUpActive && _catchUpIgnoreConditions) || _debugger?.ForceTargetCondition(group.Id, _runtime.EntryIndex) == true || _runtime.EvaluateCondition(entry.Condition)) && entry is PrimitiveEntry primitive)
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

        if (_debugger?.BeforeEntry(group.Id, _runtime.EntryIndex) == true) return;
        if (_catchUpActive && _catchUpTarget == CatchUpTarget.GroupEnd)
        {
            _catchUpActive = false;
            _catchUpStopAtBoundary = true;
            _advanceBoundary = false;
            UpdateLastStableSnapshot();
            return;
        }

        _runtime.SetEntryIndex(0);
        _active.EndGroup();
        MoveToNext();
        RemoveCompleted();
        UpdateLastStableSnapshot();
    }

    private PrimitiveInstance Dispatch(Group group, PrimitiveEntry entry)
    {
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
        if (_debugger?.Replaying == true) { JumpFrom(branch.Id, 0); return; }
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
        if (_debugger?.Replaying == true) { JumpFrom(branch.Id, 0); return; }
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
            if (_debugger?.Paused == true)
            {
                pending.DebugSelection = selected;
                return;
            }
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
            await ContinueUntilBlockedAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _flowError = exception;
            IsRunning = false;
        }
        finally
        {
            FinishDispatch();
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
            FinishDispatch();
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
                await ContinueUntilBlockedAsync().ConfigureAwait(false);
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
            FinishDispatch();
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
        _notifications.State(new GameEngineStateChange(state, state == GameEngineState.Faulted ? _flowError : null));
    }

    // Called with the execution gate held; observers are always invoked after release.
    private void FinishDispatch()
    {
        try { CaptureStateChange(); }
        finally { _dispatchGate.Release(); }
        PublishStateChanges();
    }

    private void PublishStateChanges() => _notifications.Publish();

    private void RemoveCompleted() => _active.RemoveCompleted(ReconcileAutoBlocker);

    private void ClearActive()
    {
        _active.Clear();
        CancelPlaybackTimer();
    }

    private void JumpFrom(string nodeId, int outlet)
    {
        if (_debugger?.Replaying == true) outlet = _debugger.TakeOutlet(nodeId);
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
        _notifications.Checkpoint(_lastStableSnapshot);
    }

    private void CancelPendingChoice()
    {
        var pending = _pendingChoice;
        _pendingChoice = null;
        if (pending is null)
            return;
        CancelDetachedScope(pending.Cancellation);
    }

    private static void CancelDetachedScope(CancellationTokenSource cancellation)
    {
        try { cancellation.Cancel(); }
        catch (AggregateException error)
        {
            try { GameLog.Logger.Warning(error, "Detached run cancellation callback failed"); }
            catch (Exception) { /* The obsolete scope must still be released. */ }
        }
        finally { cancellation.Dispose(); }
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
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public int? DebugSelection { get; set; }
    }
}
