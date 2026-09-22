using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Runtime;

namespace GalNet.Runtime.Engine;

/// <summary>Owns story flow and all active primitive instances for one game run.</summary>
public sealed class GameEngine : IDisposable
{
    private readonly Graph _graph;
    private readonly IGameRuntime _runtime;
    private readonly IGameView _view;
    private readonly IChoicePresenter? _choicePresenter;
    private readonly IGameProgressService? _progress;
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private readonly CancellationTokenSource _scopeCancellation = new();
    private readonly List<ActivePrimitive> _active = [];
    private GameSnapshot _lastStableSnapshot;
    private PendingChoice? _pendingChoice;
    private Exception? _flowError;
    private string? _executingGroupNodeId;
    private long _groupExecutionId;
    private long _nextSequence;
    private bool _disposed;

    public GameEngine(
        Graph graph,
        IGameView view,
        ITextResolver? textResolver = null,
        SettingsContainer? settings = null,
        IGameProgressService? progress = null,
        IChoicePresenter? choicePresenter = null)
        : this(
            graph,
            new GameRuntime(textResolver, graph?.RootNodeId ?? "", settings),
            view,
            progress,
            choicePresenter)
    { }

    public GameEngine(
        Graph graph,
        IGameRuntime runtime,
        IGameView view,
        IGameProgressService? progress = null,
        IChoicePresenter? choicePresenter = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _progress = progress;
        _choicePresenter = choicePresenter;
        _lastStableSnapshot = _runtime.CreateSnapshot();
    }

    public event Action<GameSnapshot>? CheckpointCreated;
    public IGameRuntime Runtime => _runtime;
    public GameSnapshot LastStableSnapshot => _lastStableSnapshot;
    public string CurrentNodeId => _runtime.CurrentNodeId;
    public int EntryIndex => _runtime.EntryIndex;
    public bool IsRunning { get; private set; }

    /// <summary>Applies one player advance and runs until the next blocking boundary.</summary>
    public async Task<bool> AdvanceAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowFlowError();
            IsRunning = !_runtime.IsGameEnded;
            RemoveCompleted();
            UpdateLastStableSnapshot();

            if (_pendingChoice is not null || !IsRunning)
                return IsRunning;

            var blocker = EarliestBlocker();
            if (blocker is not null)
            {
                foreach (var candidate in SkipCandidates(blocker).ToArray())
                    candidate.Instance.Skip();

                RemoveCompleted();
                UpdateLastStableSnapshot();
                if (HasBlockingInstance())
                    return true;
            }

            ContinueUntilBlocked();
            ThrowFlowError();
            return IsRunning;
        }
        finally
        {
            _dispatchGate.Release();
        }
    }

    public GameSnapshot CreateSaveData() => _lastStableSnapshot;

    public void RestoreFrom(GameSnapshot data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ThrowIfDisposed();
        _dispatchGate.Wait();
        try
        {
            CancelPendingChoice();
            ClearActive();
            _runtime.RestoreFrom(data);
            _lastStableSnapshot = data;
            _executingGroupNodeId = null;
            _flowError = null;
            IsRunning = true;
        }
        finally
        {
            _dispatchGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _scopeCancellation.Cancel();
        _dispatchGate.Wait();
        try
        {
            CancelPendingChoice();
            ClearActive();
            IsRunning = false;
        }
        finally
        {
            _dispatchGate.Release();
            _scopeCancellation.Dispose();
        }
    }

    private void ContinueUntilBlocked()
    {
        while (IsRunning && _pendingChoice is null && !HasBlockingInstance())
        {
            _scopeCancellation.Token.ThrowIfCancellationRequested();
            if (_runtime.IsGameEnded)
            {
                IsRunning = false;
                break;
            }

            var node = _graph.Nodes.Find(candidate => candidate.Id == _runtime.CurrentNodeId);
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
    }

    private void ProcessGroup(Group group)
    {
        BeginGroupExecution(group);
        while (_runtime.EntryIndex < group.Entries.Count && !HasBlockingInstance())
        {
            var entry = group.Entries[_runtime.EntryIndex];
            PrimitiveInstance? instance = null;
            if (_runtime.EvaluateCondition(entry.Condition) && entry is PrimitiveEntry primitive)
                instance = Dispatch(group, primitive);
            else if (entry is not PrimitiveEntry)
                GameLog.Logger.Warning("Runtime ignored composite entry '{EntryType}'.", entry.Type);

            _runtime.SetEntryIndex(_runtime.EntryIndex + 1);
            if (instance is not null)
                Track(instance);
            RemoveCompleted();
            UpdateLastStableSnapshot();
        }

        if (_runtime.EntryIndex < group.Entries.Count || HasBlockingInstance())
            return;

        _runtime.SetEntryIndex(0);
        _executingGroupNodeId = null;
        MoveToNext();
        RemoveCompleted();
        UpdateLastStableSnapshot();
    }

    private PrimitiveInstance? Dispatch(Group group, PrimitiveEntry entry)
    {
        if (!entry.IsGeneric)
        {
            GameLog.Logger.Warning("Runtime ignored non-generic primitive '{EntryType}'.", entry.Type);
            return null;
        }

        PrimitiveInstance? instance;
        try
        {
            instance = _view.Dispatch(entry, _runtime, _scopeCancellation.Token);
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            GameLog.Logger.Error(exception, "Primitive '{PrimitiveType}' failed to dispatch and execution continued.", entry.Type);
            return null;
        }

        if (instance is null)
        {
            GameLog.Logger.Warning("Primitive '{PrimitiveType}' is not registered.", entry.Type);
            return null;
        }

        if (entry.Type == "dialogue.text")
            _progress?.MarkRead(group.Id, entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return instance;
    }

    private void Track(PrimitiveInstance instance)
    {
        var active = new ActivePrimitive(_groupExecutionId, ++_nextSequence, instance);
        _active.Add(active);
        if (instance.IsBlocking && !instance.IsCompleted)
            instance.Completed += OnBlockingCompleted;
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
        _ = CompleteChoiceAsync(pending, selection);
    }

    private async Task CompleteChoiceAsync(PendingChoice pending, Task<int> selection)
    {
        int selected;
        try
        {
            selected = await selection.ConfigureAwait(false);
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
            if (!ReferenceEquals(_pendingChoice, pending) || _disposed)
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
            _dispatchGate.Release();
        }
    }

    private async Task RecordFlowErrorAsync(PendingChoice pending, Exception exception)
    {
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_pendingChoice, pending) || _disposed)
                return;
            _pendingChoice = null;
            pending.Cancellation.Dispose();
            _flowError = exception;
            IsRunning = false;
        }
        finally
        {
            _dispatchGate.Release();
        }
    }

    private void OnBlockingCompleted(PrimitiveInstance instance) =>
        _ = ContinueAfterBlockingCompletionAsync(instance);

    private async Task ContinueAfterBlockingCompletionAsync(PrimitiveInstance instance)
    {
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || !_active.Any(item => ReferenceEquals(item.Instance, instance)))
                return;
            RemoveCompleted();
            UpdateLastStableSnapshot();
            if (_pendingChoice is null && !HasBlockingInstance())
                ContinueUntilBlocked();
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
            _dispatchGate.Release();
        }
    }

    private IEnumerable<ActivePrimitive> SkipCandidates(ActivePrimitive blocker)
    {
        if (blocker.Instance.BatchId is null)
        {
            if (!blocker.Instance.IsCompleted && blocker.Instance.IsSkippable)
                yield return blocker;
            yield break;
        }

        foreach (var item in _active.OrderBy(item => item.Sequence))
            if (item.GroupExecutionId == blocker.GroupExecutionId &&
                string.Equals(item.Instance.BatchId, blocker.Instance.BatchId, StringComparison.Ordinal) &&
                !item.Instance.IsCompleted && item.Instance.IsSkippable)
                yield return item;
    }

    private ActivePrimitive? EarliestBlocker() => _active
        .Where(item => item.Instance.IsBlocking && !item.Instance.IsCompleted)
        .OrderBy(item => item.Sequence)
        .FirstOrDefault();

    private bool HasBlockingInstance() => _active.Any(item =>
        item.Instance.IsBlocking && !item.Instance.IsCompleted);

    private void RemoveCompleted()
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var item = _active[index];
            if (!item.Instance.IsCompleted)
                continue;
            item.Instance.Completed -= OnBlockingCompleted;
            _active.RemoveAt(index);
        }
    }

    private void ClearActive()
    {
        foreach (var item in _active)
            item.Instance.Completed -= OnBlockingCompleted;
        _active.Clear();
    }

    private void BeginGroupExecution(Group group)
    {
        if (string.Equals(_executingGroupNodeId, group.Id, StringComparison.Ordinal))
            return;
        _executingGroupNodeId = group.Id;
        _groupExecutionId++;
    }

    private void JumpFrom(string nodeId, int outlet)
    {
        var edge = _graph.Edges.Find(candidate =>
            candidate.FromNodeId == nodeId && candidate.FromOutlet == outlet);
        if (edge is null)
            IsRunning = false;
        else
            _runtime.JumpTo(edge.ToNodeId);
    }

    private void MoveToNext() => JumpFrom(_runtime.CurrentNodeId, 0);

    private void UpdateLastStableSnapshot()
    {
        if (HasBlockingInstance() || _pendingChoice is not null)
            return;
        _lastStableSnapshot = _runtime.CreateSnapshot();
        CheckpointCreated?.Invoke(_lastStableSnapshot);
    }

    private void CancelPendingChoice()
    {
        var pending = _pendingChoice;
        _pendingChoice = null;
        if (pending is null)
            return;
        pending.Cancellation.Cancel();
        pending.Cancellation.Dispose();
    }

    private void ThrowFlowError()
    {
        if (_flowError is { } exception)
            throw new InvalidOperationException("The game flow failed.", exception);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GameEngine));
    }

    private sealed record ActivePrimitive(
        long GroupExecutionId,
        long Sequence,
        PrimitiveInstance Instance);

    private sealed record VisibleChoice(int Outlet, string Text, string Condition);

    private sealed record PendingChoice(
        string BranchId,
        IReadOnlyList<VisibleChoice> Options,
        CancellationTokenSource Cancellation);
}
