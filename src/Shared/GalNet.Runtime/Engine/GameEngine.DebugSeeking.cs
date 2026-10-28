using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Runtime.Runtime;

namespace GalNet.Runtime.Engine;

public enum DebugReplayBehavior { Unsupported, Scene, Ignore }
public sealed class GameDebugOptions
{
    public long MaxEntrancePayloadBytes { get; init; } = 64L * 1024 * 1024;
    public int MaxEntranceGroups { get; init; } = 256;
    public IReadOnlyDictionary<string, DebugReplayBehavior> Instructions { get; init; } = new Dictionary<string, DebugReplayBehavior>();
    public Func<IGameContext, CancellationToken, Task>? SynchronizePresentation { get; init; }
}
public sealed record GameDebugRouteEdge(string From, int Outlet, string To);
public sealed record GameDebugSeek(string GroupId, int EntryIndex, GameDebugRouteEdge[] Route, int? TargetEnd = null, bool InitialScene = false);

public sealed partial class GameEngine
{
    public sealed partial class DebugSession
    {
        private readonly GameDebugOptions _options;
        private readonly GameSnapshot _initialScene;
        private readonly Dictionary<string, GameSnapshot> _entrances = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _seeks = new(1, 1);
        private CancellationTokenSource? _seekCancellation;
        private TaskCompletionSource? _seekCompletion;
        internal Task PendingSeek => Volatile.Read(ref _seekCompletion)?.Task ?? Task.CompletedTask;
        private bool _suppressEntrance;
        private GameDebugSeek? _seek;
        private bool _preparingReplay;
        private Queue<GameDebugRouteEdge> _route = new();
        internal bool Replaying => _seek is not null;
        internal bool SuppressReadProgress => _preparingReplay || Replaying;

        internal void OnExternalRestore(bool debugRestore)
        {
            _forcedTarget = null;
            _stepBoundary = null;
            _skipBreakpoint = null;
            Interlocked.Increment(ref _generation);
            if (!debugRestore)
            {
                CancelSeek(); ClearEntrances();
                _seek = null; _route.Clear(); _preparingReplay = false; _suppressEntrance = false;
            }
            Paused = debugRestore;
        }
        internal void CaptureEntrance(string group)
        {
            if (_suppressEntrance) { _suppressEntrance = false; return; }
            if (Replaying) return;
            if (_engine.EntryIndex == 0) StoreEntrance(group);
        }
        private GameSnapshot CaptureScene()
        {
            var snapshot = _engine._runtime.CreateSnapshot();
            return _engine.StampContent(new GameSnapshot { NodeId = snapshot.NodeId, EntryIndex = snapshot.EntryIndex, States = snapshot.States });
        }
        internal bool BeforeEntry(string group, int index)
        {
            if (_seek is null) return BeforeNormalEntry(group, index);
            if (_seek.GroupId != group || _seek.EntryIndex != index || _route.Count > 0) return false;
            Paused = true;
            _engine._catchUpActive = false;
            _engine._catchUpStopAtBoundary = true;
            return true;
        }
        internal bool IgnoreInstruction(string type) => _options.Instructions.GetValueOrDefault(type) switch
        {
            DebugReplayBehavior.Scene => false,
            DebugReplayBehavior.Ignore => true,
            _ => throw new NotSupportedException($"Instruction '{type}' does not declare debug scene replay support.")
        };
        internal int TakeOutlet(string node)
        {
            if (!_route.TryDequeue(out var edge) || edge.From != node)
                throw new InvalidOperationException("The selected debug route does not cover this exit.");
            return edge.Outlet;
        }
        public void CancelSeek() => _seekCancellation?.Cancel();

        public async Task SeekAsync(long generation, GameDebugSeek target, CancellationToken cancellationToken = default)
        {
            await _seeks.WaitAsync(cancellationToken).ConfigureAwait(false);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _seekCompletion, completion);
            _seekCancellation = cancellation;
            var began = false;
            var changed = false;
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var cacheHit = false;
            _replayedInstructions = 0;
            var ownedGeneration = generation;
            Exception? failure = null;
            try
            {
                await _engine.CancelCatchUpAndWaitAsync(cancellation.Token).ConfigureAwait(false);
                GameSnapshot baseline;
                var fromCurrent = false;
                Task[] oldPresentations;
                await _engine._dispatchGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
                try
                {
                    Check(generation);
                    if (_engine._view.RequiresStateRestore && _options.SynchronizePresentation is null && _engine._catchUpPresentation is null)
                        throw new NotSupportedException("The host must provide debug presentation restoration.");
                    if (_engine._navigation.FindNode(target.GroupId) is not Group group || target.EntryIndex < 0 || target.EntryIndex > group.Entries.Count)
                        throw new ArgumentException("Invalid debug target.", nameof(target));
                    var start = target.Route.FirstOrDefault()?.From ?? target.GroupId;
                    var targetEnd = target.TargetEnd ?? Math.Min(target.EntryIndex + 1, group.Entries.Count);
                    if (targetEnd < target.EntryIndex || targetEnd > group.Entries.Count) throw new ArgumentException("Invalid target author span.", nameof(target));
                    if (target.InitialScene)
                    {
                        if (target.Route.Length != 0) throw new ArgumentException("An isolated test start cannot include a route.", nameof(target));
                        baseline = new GameSnapshot { NodeId = target.GroupId, EntryIndex = 0, States = _initialScene.States };
                    }
                    else if (start == _engine.CurrentNodeId && (start != target.GroupId || target.EntryIndex >= _engine.EntryIndex))
                    {
                        baseline = _engine._runtime.CreateSnapshot();
                        fromCurrent = true;
                    }
                    else if (_entrances.TryGetValue(start, out var entrance))
                    {
                        baseline = SnapshotCopy.Clone(entrance);
                        _entranceUse[start] = ++_cacheClock;
                        cacheHit = true;
                    }
                    else if (_initialScene.NodeId == start) baseline = SnapshotCopy.Clone(_initialScene);
                    else throw new InvalidOperationException("No entrance scene is cached. Choose a route from the game entry.");
                    ValidateRoute(target, baseline);
                    _preparingReplay = true;
                    Paused = true;
                    _engine.SetPlaybackModeCore(PlaybackMode.Manual);
                    _engine.CancelPlaybackTimer();
                    if (_engine._catchUpPresentation is { } presentation)
                    {
                        began = true;
                        await presentation.BeginCatchUpAsync(cancellation.Token).ConfigureAwait(false);
                    }
                    var old = _engine._active.ToArray();
                    foreach (var item in old) item.Instance.ApplyPlaybackMode(PrimitiveInvocationMode.CatchUp);
                    oldPresentations = old.Select(item => item.Instance.PresentationCompletion).ToArray();
                }
                finally { _engine._dispatchGate.Release(); _engine.PublishStateChanges(); }

                await Task.WhenAll(oldPresentations).WaitAsync(cancellation.Token).ConfigureAwait(false);
                await _engine._dispatchGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
                try
                {
                    Check(generation);
                    // Active transitions may commit their terminal scene while being
                    // compressed. A forward baseline must include that committed state.
                    var current = _engine._runtime.CreateSnapshot();
                    if (fromCurrent) baseline = current;
                    baseline = _engine.StampContent(new GameSnapshot
                    {
                        NodeId = baseline.NodeId,
                        EntryIndex = baseline.EntryIndex,
                        States = baseline.States,
                        Variables = current.Variables
                    });
                }
                finally { _engine._dispatchGate.Release(); }
                ownedGeneration = await _engine.RestoreFromCoreAsync(baseline, this, generation, cancellation.Token).ConfigureAwait(false);
                changed = true;

                await _engine._dispatchGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
                try
                {
                    Check(ownedGeneration);
                    _seek = target;
                    _route = new Queue<GameDebugRouteEdge>(target.Route);
                    _suppressEntrance = true;
                    _engine._dispatchRuntime = new RunScopedRuntime(_engine._runtime, _engine._scopeCancellation.Token, ignoreVariableWrites: true);
                    _engine._restorationPending = false;
                    _engine._started = true;
                    _engine.IsRunning = true;
                    _engine._catchUpTarget = CatchUpTarget.DebugPosition;
                    _engine._catchUpIgnoreConditions = true;
                    _engine._catchUpActive = true;
                    Paused = false;
                }
                finally { _engine._dispatchGate.Release(); }
                var budget = 100000;
                while (true)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await _engine._dispatchGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
                    bool waiting;
                    try
                    {
                        Check(ownedGeneration);
                        if (budget-- == 0) throw new InvalidOperationException("Debug replay exceeded its execution budget.");
                        _engine._catchUpBudget = 128;
                        _engine.RemoveCompleted();
                        await _engine.ContinueUntilBlockedAsync(cancellation.Token).ConfigureAwait(false);
                        _engine.ThrowFlowError();
                        if (Paused)
                        {
                            _forcedTarget = new(target.GroupId, target.EntryIndex, target.TargetEnd ?? Math.Min(target.EntryIndex + 1,
                                ((Group)_engine._navigation.FindNode(target.GroupId)!).Entries.Count));
                            break;
                        }
                        if (!_engine.IsRunning) throw new InvalidOperationException("The debug route ended before the target.");
                        waiting = _engine._active.HasBlockingInstance();
                    }
                    finally { _engine.FinishDispatch(); }
                    if (waiting) await _engine._catchUpSignal.WaitAsync(cancellation.Token).ConfigureAwait(false);
                    else await Task.Yield();
                }
            }
            catch (Exception error)
            {
                failure = error;
                if (changed && Generation == ownedGeneration)
                {
                    await _engine._dispatchGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        Paused = true;
                        if (error is not OperationCanceledException) { _engine._flowError = error; _engine.IsRunning = false; }
                    }
                    finally { _engine._dispatchGate.Release(); }
                }
            }
            finally
            {
                await _engine._dispatchGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    _seek = null;
                    _preparingReplay = false;
                    _suppressEntrance = false;
                    if (changed && Generation == ownedGeneration)
                    {
                        _engine._catchUpActive = false;
                        _engine._catchUpStopAtBoundary = false;
                        _engine._dispatchRuntime = new RunScopedRuntime(_engine._runtime, _engine._scopeCancellation.Token);
                    }
                    if (began && Generation == ownedGeneration && !_engine._scopeCancellation.IsCancellationRequested && _engine._catchUpPresentation is { } presentation)
                        await presentation.EndCatchUpAsync(_engine._dispatchRuntime, _engine._scopeCancellation.Token).ConfigureAwait(false);
                    else if (changed && Generation == ownedGeneration && _options.SynchronizePresentation is { } synchronize)
                        await synchronize(_engine._dispatchRuntime, _engine._scopeCancellation.Token).ConfigureAwait(false);
                    if (changed && Generation == ownedGeneration)
                    {
                        _lastReplay = new(System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, _replayedInstructions, cacheHit);
                        _engine.UpdateLastStableSnapshot();
                    }
                }
                catch (Exception error)
                {
                    _engine._flowError = error; _engine.IsRunning = false;
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                finally
                {
                    _engine.FinishDispatch();
                    _seekCancellation = null;
                    cancellation.Dispose();
                    completion.TrySetResult();
                    _seeks.Release();
                }
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void ValidateRoute(GameDebugSeek target, GameSnapshot baseline)
        {
            if (target.Route.Length > 1024) throw new ArgumentException("Debug route is too long.", nameof(target));
            var nodes = new List<(string Id, int Start, int End)>();
            var current = baseline.NodeId;
            var index = baseline.EntryIndex;
            foreach (var edge in target.Route)
            {
                if (edge.From != current || _engine._navigation.FindTarget(current, edge.Outlet) != edge.To)
                    throw new ArgumentException("Every debug route edge must be a direct successor.", nameof(target));
                if (_engine._navigation.FindNode(current) is Group group) nodes.Add((current, index, group.Entries.Count));
                current = edge.To;
                index = 0;
            }
            if (current != target.GroupId || index > target.EntryIndex) throw new ArgumentException("Debug route does not reach the target.", nameof(target));
            nodes.Add((current, index, target.EntryIndex));
            foreach (var node in nodes)
                if (_engine._navigation.FindNode(node.Id) is Group group)
                    foreach (var entry in group.Entries.Skip(node.Start).Take(node.End - node.Start)) _ = IgnoreInstruction(entry.Type);
        }
    }
}
