using DynamicLocalization.Core;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Handler;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Compilation;
using GalNet.Runtime.Handlers;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Runtime;
using Serilog;
using GalNet.Core.Services;

namespace GalNet.Runtime.Engine;

public sealed class GameEngine
{
    private readonly Graph _graph;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<SimpleEntry>> _compiled;
    private readonly EntryHandlerRegistry _registry;
    private readonly IGameRuntime _runtime;
    private readonly IGameProgressService? _progress;

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
        IGameGraphCompiler? compiler = null,
        EntryHandlerRegistry? registry = null,
        IGameProgressService? progress = null)
    {
        _graph = graph;
        _compiled = (compiler ?? GameGraphCompiler.Default).Compile(graph);
        _registry = registry ?? EntryHandlerRegistry.CreateDefault();
        _runtime = new GameRuntime(view, i18n, graph.RootNodeId, settings);
        _progress = progress;
    }

    public GameEngine(
        Graph graph,
        IGameRuntime runtime,
        IGameGraphCompiler? compiler = null,
        EntryHandlerRegistry? registry = null,
        IGameProgressService? progress = null)
    {
        _graph = graph;
        _compiled = (compiler ?? GameGraphCompiler.Default).Compile(graph);
        _registry = registry ?? EntryHandlerRegistry.CreateDefault();
        _runtime = runtime;
        _progress = progress;
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
        var entries = _compiled.GetValueOrDefault(group.Id, Array.Empty<SimpleEntry>());
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

            if (handler.IsBlocking)
                await ExecuteBlockingEntryAsync(handler, entry, ct);
            else
                ExecuteNonBlockingEntry(handler, entry);
        }

        _runtime.SetEntryIndex(0);
        MoveToNext();
    }

    private async Task ExecuteBlockingEntryAsync(EntryHandler handler, SimpleEntry entry, CancellationToken ct)
    {
        var ctx = new EntryContext { Entry = entry, Runtime = _runtime };

        handler.Start(ctx);
        if (entry.Type == "text") _progress?.MarkRead(groupId: _runtime.CurrentNodeId, entry.Id);
        CheckpointCreated?.Invoke(CreateSaveData());

        while (!handler.IsCompleted(ctx))
        {
            await _runtime.View!.WaitForClickAsync(ct);
            handler.Interrupt(ctx);
        }

        handler.Complete(ctx);
    }

    private void ExecuteNonBlockingEntry(EntryHandler handler, SimpleEntry entry)
    {
        var ctx = new EntryContext { Entry = entry, Runtime = _runtime };
        handler.Start(ctx);
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
        var selected = await _runtime.View!.WaitForChoiceAsync("default_choice", texts, ct);

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
        IsRunning = true;
    }
}
