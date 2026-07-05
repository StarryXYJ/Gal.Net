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
using GalNet.Runtime.Runtime;

namespace GalNet.Runtime.Engine;

public sealed class GameEngine
{
    private readonly Graph _graph;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<SimpleEntry>> _compiled;
    private readonly EntryHandlerRegistry _registry;
    private readonly IGameRuntime _runtime;

    public IGameRuntime Runtime => _runtime;

    public string CurrentNodeId { get => _runtime.CurrentNodeId; private set => _runtime.CurrentNodeId = value; }

    public int EntryIndex { get => _runtime.EntryIndex; private set => _runtime.EntryIndex = value; }

    public bool IsRunning { get; private set; }

    public GameEngine(
        Graph graph,
        IGameView view,
        ICultureService? i18n = null,
        SettingsContainer? settings = null,
        IGameGraphCompiler? compiler = null,
        EntryHandlerRegistry? registry = null)
    {
        _graph = graph;
        _compiled = (compiler ?? GameGraphCompiler.Default).Compile(graph);
        _registry = registry ?? EntryHandlerRegistry.CreateDefault();
        _runtime = new GameRuntime(view, i18n, graph.RootNodeId, settings);
    }

    public GameEngine(
        Graph graph,
        IGameRuntime runtime,
        IGameGraphCompiler? compiler = null,
        EntryHandlerRegistry? registry = null)
    {
        _graph = graph;
        _compiled = (compiler ?? GameGraphCompiler.Default).Compile(graph);
        _registry = registry ?? EntryHandlerRegistry.CreateDefault();
        _runtime = runtime;
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
                {
                    var entries = _compiled.GetValueOrDefault(group.Id, Array.Empty<SimpleEntry>());
                    for (; _runtime.EntryIndex < entries.Count; _runtime.EntryIndex++)
                    {
                        var entry = entries[_runtime.EntryIndex];

                        if (!_runtime.EvaluateCondition(entry.Condition))
                            continue;

                        var handler = _registry.Resolve(entry.Type);
                        if (handler == null)
                            continue;

                        var ctx = new EntryContext { Entry = entry, Runtime = _runtime };

                        if (handler.IsBlocking)
                        {
                            handler.Start(ctx);
                            while (!handler.IsCompleted(ctx))
                            {
                                await _runtime.View!.WaitForClickAsync(ct);
                                handler.Interrupt(ctx);
                            }

                            handler.Complete(ctx);
                        }
                        else
                        {
                            handler.Start(ctx);
                        }
                    }

                    _runtime.EntryIndex = 0;
                    MoveToNext();
                    break;
                }

                case Branch branch:
                {
                    if (branch.BranchType == BranchType.Choice)
                    {
                        var visibleOptions = branch.Options
                            .Select((o, i) => (Option: o, Index: i))
                            .Where(x => _runtime.EvaluateCondition(x.Option.Condition))
                            .ToList();

                        if (visibleOptions.Count == 0)
                        {
                            MoveToNext();
                            break;
                        }

                        string Resolve(string key) => _runtime.I18n?[key] ?? key;
                        var texts = visibleOptions.Select(x => Resolve(x.Option.Text)).ToArray();
                        var selected = await _runtime.View!.WaitForChoiceAsync("default_choice", texts, ct);

                        if (selected >= 0 && selected < visibleOptions.Count)
                        {
                            var targetEdge = _graph.Edges
                                .Find(e => e.FromNodeId == branch.Id && e.FromOutlet == visibleOptions[selected].Index);
                            if (targetEdge != null)
                            {
                                _runtime.CurrentNodeId = targetEdge.ToNodeId;
                                _runtime.EntryIndex = 0;
                            }
                        }
                    }
                    else
                    {
                        var matched = false;
                        for (var i = 0; i < branch.Conditions.Count; i++)
                        {
                            if (_runtime.EvaluateCondition(branch.Conditions[i].Expression))
                            {
                                var targetEdge = _graph.Edges
                                    .Find(e => e.FromNodeId == branch.Id && e.FromOutlet == i);
                                if (targetEdge != null)
                                {
                                    _runtime.CurrentNodeId = targetEdge.ToNodeId;
                                    _runtime.EntryIndex = 0;
                                }

                                matched = true;
                                break;
                            }
                        }

                        if (!matched)
                            MoveToNext();
                    }

                    break;
                }
            }
        }

        return false;
    }

    private void MoveToNext()
    {
        var edge = _graph.Edges.Find(e => e.FromNodeId == _runtime.CurrentNodeId && e.FromOutlet == 0);
        if (edge != null)
        {
            _runtime.CurrentNodeId = edge.ToNodeId;
            _runtime.EntryIndex = 0;
        }
        else
        {
            IsRunning = false;
        }
    }

    public GameSnapshot CreateSaveData()
    {
        return _runtime.CreateSnapshot();
    }

    public void RestoreFrom(GameSnapshot data)
    {
        _runtime.RestoreFrom(data);
        IsRunning = true;
    }
}
