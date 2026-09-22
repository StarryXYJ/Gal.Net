using System.Text.Json;
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

/// <summary>Drives compiled primitive envelopes through one Game Scope dispatcher.</summary>
public sealed class GameEngine
{
    private readonly Graph _graph;
    private readonly IGameRuntime _runtime;
    private readonly IGameView _view;
    private readonly OperationManager _operations;
    private readonly IGameProgressService? _progress;
    private GameSnapshot _lastStableSnapshot;

    public event Action<GameSnapshot>? CheckpointCreated;
    public IGameRuntime Runtime => _runtime;
    public OperationManager Operations => _operations;
    public GameSnapshot LastStableSnapshot => _lastStableSnapshot;
    public string CurrentNodeId => _runtime.CurrentNodeId;
    public int EntryIndex => _runtime.EntryIndex;
    public bool IsRunning { get; private set; }

    public GameEngine(Graph graph, IGameView view, ITextResolver? textResolver = null,
        SettingsContainer? settings = null, IGameProgressService? progress = null, OperationManager? operations = null)
        : this(graph, new GameRuntime(textResolver, graph.RootNodeId, settings), view, progress, operations) { }

    public GameEngine(Graph graph, IGameRuntime runtime, IGameView view,
        IGameProgressService? progress = null, OperationManager? operations = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _progress = progress;
        _operations = operations ?? new OperationManager();
        _lastStableSnapshot = _runtime.CreateSnapshot();
    }

    public async Task<bool> StepAsync(CancellationToken ct = default)
    {
        IsRunning = true;
        while (IsRunning)
        {
            ct.ThrowIfCancellationRequested();
            if (_runtime.IsGameEnded) return IsRunning = false;
            var node = _graph.Nodes.Find(node => node.Id == _runtime.CurrentNodeId);
            if (node is null) return IsRunning = false;
            if (node is Group group) await ProcessGroupAsync(group, ct);
            else if (node is Branch branch) await ProcessBranchAsync(branch, ct);
        }
        return false;
    }

    public Task<bool> SkipNextBatchAsync() => _operations.SkipNextBatchAsync();

    private async Task ProcessGroupAsync(Group group, CancellationToken ct)
    {
        while (_runtime.EntryIndex < group.Entries.Count)
        {
            ct.ThrowIfCancellationRequested();
            var entry = group.Entries[_runtime.EntryIndex];
            if (_runtime.EvaluateCondition(entry.Condition) && entry is PrimitiveEntry primitive)
                await DispatchAsync(primitive, PrimitiveInvocationOrigin.GroupEntry, entry.Id.ToString(), true, ct);
            else if (entry is not PrimitiveEntry)
                GameLog.Logger.Warning("Runtime ignored non-primitive entry '{EntryType}'.", entry.Type);

            _runtime.SetEntryIndex(_runtime.EntryIndex + 1);
            UpdateLastStableSnapshot();
        }
        _runtime.SetEntryIndex(0);
        MoveToNext();
        UpdateLastStableSnapshot();
    }

    private async Task DispatchAsync(PrimitiveEntry entry, PrimitiveInvocationOrigin origin, string sourceId, bool allowCheckpoint, CancellationToken ct)
    {
        if (!entry.IsGeneric)
        {
            GameLog.Logger.Warning("Runtime ignored non-generic primitive '{EntryType}'.", entry.Type);
            return;
        }
        if (!_view.TryGetDescriptor(entry.Type, out var descriptor))
        {
            GameLog.Logger.Warning("Primitive '{PrimitiveType}' is not registered.", entry.Type);
            return;
        }
        if (allowCheckpoint && origin == PrimitiveInvocationOrigin.GroupEntry && descriptor!.CreatesCheckpoint && IsStable())
        {
            if (entry.Type == "dialogue.text") _progress?.MarkRead(_runtime.CurrentNodeId, sourceId);
            CheckpointCreated?.Invoke(_lastStableSnapshot);
        }

        var control = new PrimitiveExecutionControl();
        var invocation = new PrimitiveInvocation(entry.Type,
            new PrimitiveContext { Runtime = _runtime, Origin = origin, SourceId = sourceId }, entry.Arguments);
        var dispatch = _view.Dispatch(invocation, control, ct);
        var operation = _operations.Track(entry.Type, dispatch, control);
        if (dispatch.Status == PrimitiveDispatchStatus.Skipped) return;
        if (!dispatch.Policy.Blocking)
        {
            _ = ObserveAndCheckpointAsync(entry.Type, dispatch.Completion);
            return;
        }
        await ObserveAsync(entry.Type, dispatch.Completion, ct);
        UpdateLastStableSnapshot();
    }

    private async Task ObserveAsync(string typeId, Task<PrimitiveResult> completion, CancellationToken? scopeCancellation = null)
    {
        try
        {
            var result = await completion.ConfigureAwait(false);
            if (result.Status == PrimitiveResultStatus.Failed)
                GameLog.Logger.Error("Primitive '{PrimitiveType}' reported an expected failure.", typeId);
        }
        catch (OperationCanceledException) when (scopeCancellation?.IsCancellationRequested == true)
        {
            throw;
        }
        catch (Exception exception)
        {
            GameLog.Logger.Error(exception, "Primitive '{PrimitiveType}' failed and execution continued.", typeId);
        }
    }

    private async Task ObserveAndCheckpointAsync(string typeId, Task<PrimitiveResult> completion)
    {
        await ObserveAsync(typeId, completion).ConfigureAwait(false);
        UpdateLastStableSnapshot();
    }

    private async Task ProcessBranchAsync(Branch branch, CancellationToken ct)
    {
        if (branch.BranchType != BranchType.Choice) { ProcessConditionBranch(branch); return; }
        var visible = branch.Options.Select((option, index) => (option, index)).Where(item => _runtime.EvaluateCondition(item.option.Condition)).ToArray();
        if (visible.Length == 0) { MoveToNext(); return; }
        var arguments = JsonSerializer.SerializeToElement(new { widgetId = "default_choice", options = visible.Select(item => _runtime.TextResolver.Resolve(item.option.Text)).ToArray() });
        var choice = new PrimitiveEntry("interaction.choice", arguments);
        if (_view.TryGetDescriptor(choice.Type, out var descriptor) && descriptor!.CreatesCheckpoint && IsStable())
            CheckpointCreated?.Invoke(_lastStableSnapshot);
        var control = new PrimitiveExecutionControl();
        var dispatch = _view.Dispatch(new PrimitiveInvocation(choice.Type, new PrimitiveContext { Runtime = _runtime, Origin = PrimitiveInvocationOrigin.Internal }, choice.Arguments), control, ct);
        _operations.Track(choice.Type, dispatch, control);
        var result = PrimitiveResult.Empty;
        if (dispatch.Status == PrimitiveDispatchStatus.Accepted)
        {
            try
            {
                result = await dispatch.Completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                GameLog.Logger.Error(exception, "Choice primitive failed and execution continued.");
            }
        }
        if (result.Status == PrimitiveResultStatus.Failed)
            GameLog.Logger.Error("Choice primitive reported an expected failure.");
        if (result.Status == PrimitiveResultStatus.Succeeded && result.Value is { } value && value.TryGetInt32(out var selected) && selected >= 0 && selected < visible.Length)
        {
            var edge = _graph.Edges.Find(edge => edge.FromNodeId == branch.Id && edge.FromOutlet == visible[selected].index);
            if (edge is not null) _runtime.JumpTo(edge.ToNodeId);
        }
        UpdateLastStableSnapshot();
    }

    private void ProcessConditionBranch(Branch branch)
    {
        for (var index = 0; index < branch.Conditions.Count; index++)
            if (_runtime.EvaluateCondition(branch.Conditions[index].Expression))
            {
                var edge = _graph.Edges.Find(edge => edge.FromNodeId == branch.Id && edge.FromOutlet == index);
                if (edge is not null) _runtime.JumpTo(edge.ToNodeId);
                return;
            }
        MoveToNext();
    }

    private bool IsStable() => _operations.ActiveOperations.Count == 0;
    private void UpdateLastStableSnapshot() { if (IsStable()) _lastStableSnapshot = _runtime.CreateSnapshot(); }
    private void MoveToNext()
    {
        var edge = _graph.Edges.Find(edge => edge.FromNodeId == _runtime.CurrentNodeId && edge.FromOutlet == 0);
        if (edge is null) IsRunning = false;
        else _runtime.JumpTo(edge.ToNodeId);
    }
    public GameSnapshot CreateSaveData() => _lastStableSnapshot;
    public void RestoreFrom(GameSnapshot data) { _runtime.RestoreFrom(data); _lastStableSnapshot = data; IsRunning = true; }
}
