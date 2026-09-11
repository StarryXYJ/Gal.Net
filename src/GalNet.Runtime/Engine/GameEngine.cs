using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Handlers;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Runtime;
using Serilog;
using GalNet.Core.Services;

namespace GalNet.Runtime.Engine;

/// <summary>
/// Drives the graph one entry at a time, keeping runtime state authoritative while
/// delegating all rendering and interaction to <see cref="IGameView"/>.
/// </summary>
public sealed class GameEngine
{
    private readonly Graph _graph;
    private readonly EntryHandlerRegistry _registry;
    private readonly IGameRuntime _runtime;
    private readonly IGameView _view;
    private readonly IGameProgressService? _progress;
    private readonly TimeProvider _timeProvider;

    /// <summary>Raised at interaction boundaries, before the engine waits for input.</summary>
    public event Action<GameSnapshot>? CheckpointCreated;

    /// <summary>Mutable runtime state used by handlers and save/restore operations.</summary>
    public IGameRuntime Runtime => _runtime;
    public string CurrentNodeId => _runtime.CurrentNodeId;
    public int EntryIndex => _runtime.EntryIndex;
    public bool IsRunning { get; private set; }

    /// <summary>Creates an engine with a new runtime rooted at the graph's entry node.</summary>
    /// <param name="graph">The compiled story graph to execute.</param>
    /// <param name="view">Presentation and input adapter; it does not own game state.</param>
    /// <param name="textResolver">Optional localization resolver for the new runtime.</param>
    /// <param name="settings">Optional initial settings for the new runtime.</param>
    /// <param name="registry">Optional entry-handler registry; the built-in registry is used by default.</param>
    /// <param name="progress">Optional service notified when checkpointable content is read.</param>
    /// <param name="timeProvider">Clock supplied to time-based handlers.</param>
    public GameEngine(
        Graph graph,
        IGameView view,
        ITextResolver? textResolver = null,
        SettingsContainer? settings = null,
        EntryHandlerRegistry? registry = null,
        IGameProgressService? progress = null,
        TimeProvider? timeProvider = null)
    {
        _graph = graph;
        _registry = registry ?? EntryHandlerRegistry.CreateDefault();
        _runtime = new GameRuntime(textResolver, graph.RootNodeId, settings);
        _view = view;
        _progress = progress;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Creates an engine over an existing runtime, such as one restored from a save.</summary>
    /// <param name="graph">The graph whose identifiers must match the supplied runtime.</param>
    /// <param name="runtime">Existing mutable game state.</param>
    /// <param name="view">Presentation and input adapter.</param>
    /// <param name="registry">Optional entry-handler registry; the built-in registry is used by default.</param>
    /// <param name="progress">Optional service notified when checkpointable content is read.</param>
    /// <param name="timeProvider">Clock supplied to time-based handlers.</param>
    public GameEngine(
        Graph graph,
        IGameRuntime runtime,
        IGameView view,
        EntryHandlerRegistry? registry = null,
        IGameProgressService? progress = null,
        TimeProvider? timeProvider = null)
    {
        _graph = graph;
        _registry = registry ?? EntryHandlerRegistry.CreateDefault();
        _runtime = runtime;
        _view = view;
        _progress = progress;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Continues execution until the graph ends, has no outgoing edge, or the operation is cancelled.
    /// Interaction entries suspend this method until the view supplies their input.
    /// </summary>
    /// <param name="ct">Cancels pending interaction or timed-entry work.</param>
    /// <returns><see langword="false"/> when execution stops; cancellation is propagated.</returns>
    public async Task<bool> StepAsync(CancellationToken ct = default)
    {
        IsRunning = true;

        while (IsRunning)
        {
            ct.ThrowIfCancellationRequested();

            if (_runtime.IsGameEnded)
            {
                IsRunning = false;
                return false;
            }

            var node = _graph.Nodes.Find(n => n.Id == _runtime.CurrentNodeId);
            if (node == null)
                break;

            switch (node)
            {
                case Group group:
                    await ProcessGroupAsync(group, ct);
                    break;
                case Branch branch:
                    await ProcessBranchAsync(branch, ct);
                    break;
            }
        }

        return false;
    }

    private async Task ProcessGroupAsync(Group group, CancellationToken ct)
    {
        var entries = group.Entries;
        GameLog.Logger.Information("Engine: Processing group '{GroupId}' ({EntryCount} entries, entryIndex={EntryIndex})",
            group.Id, entries.Count, _runtime.EntryIndex);

        for (; _runtime.EntryIndex < entries.Count; _runtime.SetEntryIndex(_runtime.EntryIndex + 1))
        {
            ct.ThrowIfCancellationRequested();

            var entry = entries[_runtime.EntryIndex];
            if (!_runtime.EvaluateCondition(entry.Condition))
                continue;
            if (entry is not PrimitiveEntry)
                throw new InvalidOperationException($"Runtime cannot execute non-primitive entry '{entry.Type}'. Compile the source group first.");

            var handler = _registry.Resolve(entry.Type);
            if (handler == null)
                continue;

            await ExecuteEntryAsync(handler, entry, ct);
        }

        _runtime.SetEntryIndex(0);
        MoveToNext();
    }

    private async Task ExecuteEntryAsync(EntryHandler handler, Entry entry, CancellationToken ct)
    {
        var ctx = new EntryContext { Entry = entry, Runtime = _runtime, DispatchTimelineEventAsync = DispatchTimelineEventAsync };

        if (handler.CreatesCheckpoint)
        {
            if (entry.Type == TextEntry.TypeId) _progress?.MarkRead(groupId: _runtime.CurrentNodeId, entry.Id.ToString());
            CheckpointCreated?.Invoke(CreateSaveData());
        }

        await handler.ExecuteAsync(ctx, _view, _timeProvider, ct);
    }

    private Task DispatchTimelineEventAsync(Entry entry, CancellationToken ct)
    {
        if (entry is not PrimitiveEntry)
        {
            GameLog.Logger.Warning("Animation plan event ignored because entry type '{EntryType}' is not a primitive.", entry.Type);
            return Task.CompletedTask;
        }
        var handler = _registry.Resolve(entry.Type);
        if (handler is null)
        {
            GameLog.Logger.Warning("Animation plan event ignored because entry type '{EntryType}' is unknown.", entry.Type);
            return Task.CompletedTask;
        }

        var context = new EntryContext { Entry = entry, Runtime = _runtime, DispatchTimelineEventAsync = DispatchTimelineEventAsync };
        return handler.ExecuteAsync(context, _view, _timeProvider, ct);
    }

    private async Task ProcessBranchAsync(Branch branch, CancellationToken ct)
    {
        if (branch.BranchType == BranchType.Choice)
            await ProcessChoiceBranchAsync(branch, ct);
        else
            ProcessConditionBranch(branch);
    }

    private async Task ProcessChoiceBranchAsync(Branch branch, CancellationToken ct)
    {
        var visibleOptions = branch.Options
            .Select((o, i) => (Option: o, Index: i))
            .Where(x => _runtime.EvaluateCondition(x.Option.Condition))
            .ToList();

        if (visibleOptions.Count == 0)
        {
            MoveToNext();
            return;
        }

        string Resolve(string key) => _runtime.TextResolver.Resolve(key);
        var texts = visibleOptions.Select(x => Resolve(x.Option.Text)).ToArray();

        CheckpointCreated?.Invoke(CreateSaveData());
        var selected = await _view.WaitForChoiceAsync("default_choice", texts, ct);

        if (selected >= 0 && selected < visibleOptions.Count)
        {
            var targetEdge = _graph.Edges
                .Find(e => e.FromNodeId == branch.Id && e.FromOutlet == visibleOptions[selected].Index);
            if (targetEdge != null)
                _runtime.JumpTo(targetEdge.ToNodeId);
        }
    }

    private void ProcessConditionBranch(Branch branch)
    {
        for (var i = 0; i < branch.Conditions.Count; i++)
        {
            if (!_runtime.EvaluateCondition(branch.Conditions[i].Expression))
                continue;

            var targetEdge = _graph.Edges
                .Find(e => e.FromNodeId == branch.Id && e.FromOutlet == i);
            if (targetEdge != null)
                _runtime.JumpTo(targetEdge.ToNodeId);

            return;
        }

        MoveToNext();
    }

    private void MoveToNext()
    {
        var edge = _graph.Edges.Find(e => e.FromNodeId == _runtime.CurrentNodeId && e.FromOutlet == 0);
        if (edge != null)
        {
            GameLog.Logger.Information("Engine: Moving from '{FromNodeId}' -> '{ToNodeId}'",
                _runtime.CurrentNodeId, edge.ToNodeId);
            _runtime.JumpTo(edge.ToNodeId);
        }
        else
        {
            GameLog.Logger.Information("Engine: No outgoing edge from '{NodeId}' - stopping",
                _runtime.CurrentNodeId);
            IsRunning = false;
        }
    }

    /// <summary>Captures the runtime-owned state at the current execution position.</summary>
    public GameSnapshot CreateSaveData() => _runtime.CreateSnapshot();

    /// <summary>Restores runtime state and replays every visible layer into the presentation adapter.</summary>
    /// <param name="data">A snapshot produced for a compatible graph and runtime configuration.</param>
    public void RestoreFrom(GameSnapshot data)
    {
        _runtime.RestoreFrom(data);
        foreach (var layer in _runtime.SceneState.Layers.Where(layer => layer.Visible))
            _view.ShowLayer(new LayerRenderRequest(layer.Id, layer.AssetId, layer.Transform.Clone(), layer.Z, layer.DisplayMode, layer.Opacity, layer.Color));
        IsRunning = true;
    }
}
