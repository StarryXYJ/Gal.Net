using DynamicLocalization.Core;
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

    public IGameRuntime Runtime => _runtime;
    public string CurrentNodeId => _runtime.CurrentNodeId;
    public int EntryIndex => _runtime.EntryIndex;
    public bool IsRunning { get; private set; }

    public GameEngine(
        Graph graph,
        IGameView view,
        ICultureService? i18n = null,
        SettingsContainer? settings = null,
        EntryHandlerRegistry? registry = null,
        IGameProgressService? progress = null,
        TimeProvider? timeProvider = null)
    {
        _graph = graph;
        _registry = registry ?? EntryHandlerRegistry.CreateDefault();
        _runtime = new GameRuntime(i18n, graph.RootNodeId, settings);
        _view = view;
        _progress = progress;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public GameEngine(
        Graph graph,  // 游戏文件
        IGameRuntime runtime,
        IGameView view,
        EntryHandlerRegistry? registry = null,  // 开发者定义动态变量提供
        IGameProgressService? progress = null,  //游戏进度(画廊解锁之类的)
        TimeProvider? timeProvider = null)
    {
        _graph = graph;
        _registry = registry ?? EntryHandlerRegistry.CreateDefault();
        _runtime = runtime;
        _view = view;
        _progress = progress;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

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
        var ctx = new EntryContext { Entry = entry, Runtime = _runtime };

        if (handler.CreatesCheckpoint)
        {
            if (entry.Type == TextEntry.TypeId) _progress?.MarkRead(groupId: _runtime.CurrentNodeId, entry.Id.ToString());
            CheckpointCreated?.Invoke(CreateSaveData());
        }

        await handler.ExecuteAsync(ctx, _view, _timeProvider, ct);
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

        string Resolve(string key) => _runtime.I18n?[key] ?? key;
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

    public GameSnapshot CreateSaveData() => _runtime.CreateSnapshot();

    public void RestoreFrom(GameSnapshot data)
    {
        _runtime.RestoreFrom(data);
        foreach (var layer in _runtime.SceneState.Layers.Where(layer => layer.Visible))
            _view.ShowLayer(layer.Id, layer.AssetId, layer.X, layer.Y, layer.Z);
        IsRunning = true;
    }
}
