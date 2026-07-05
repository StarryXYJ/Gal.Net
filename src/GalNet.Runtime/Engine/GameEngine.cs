using DynamicLocalization.Core;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Handler;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Handlers;
using GalNet.Runtime.Runtime;
using GalNet.Runtime.Variables;

namespace GalNet.Runtime.Engine;

/// <summary>
/// 游戏引擎 —— 核心状态机循环，驱动条目执行和节点转移。
/// 所有游戏状态统一由 GameRuntime 管理。
/// </summary>
public sealed class GameEngine
{
    private readonly Graph _graph;
    private readonly EntryHandlerRegistry _registry;
    private readonly IGameRuntime _runtime;

    /// <summary>运行时状态（位置、变量、场景等）</summary>
    public IGameRuntime Runtime => _runtime;

    /// <summary>当前节点 ID（快捷方式）</summary>
    public string CurrentNodeId { get => _runtime.CurrentNodeId; private set => _runtime.CurrentNodeId = value; }

    /// <summary>当前组内条目索引（快捷方式）</summary>
    public int EntryIndex { get => _runtime.EntryIndex; private set => _runtime.EntryIndex = value; }

    /// <summary>游戏是否正在运行</summary>
    public bool IsRunning { get; private set; }

    public GameEngine(Graph graph, IGameView view, ICultureService? i18n = null,
        SettingsContainer? settings = null)
    {
        _graph = graph;
        _registry = EntryHandlerRegistry.CreateDefault();
        _runtime = new GameRuntime(view, i18n, graph.RootNodeId, settings);
    }

    /// <summary>
    /// 使用外部 GameRuntime 构造（用于读档恢复等场景）。
    /// </summary>
    public GameEngine(Graph graph, IGameRuntime runtime)
    {
        _graph = graph;
        _registry = EntryHandlerRegistry.CreateDefault();
        _runtime = runtime;
    }

    /// <summary>编译所有 Group 条目的复杂条目 → 简单条目内存列表。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<SimpleEntry>> CompileAll(Graph graph)
    {
        var result = new Dictionary<string, IReadOnlyList<SimpleEntry>>();
        foreach (var node in graph.Nodes.OfType<Group>())
        {
            var compiled = new List<SimpleEntry>();
            foreach (var complex in node.Entries)
            {
                compiled.AddRange(complex.Compile());
            }
            result[node.Id] = compiled;
        }
        return result;
    }

    /// <summary>
    /// 运行到结束或第一个阻塞点。
    /// 返回 false 表示游戏结束，true 表示等待用户交互。
    /// </summary>
    public async Task<bool> StepAsync(CancellationToken ct = default)
    {
        IsRunning = true;
        var compiled = CompileAll(_graph);

        while (IsRunning)
        {
            ct.ThrowIfCancellationRequested();

            if (_runtime.IsGameEnded)
            {
                IsRunning = false;
                return false;
            }

            var node = _graph.Nodes.Find(n => n.Id == _runtime.CurrentNodeId);
            if (node == null) break;

            switch (node)
            {
                case Group group:
                {
                    var entries = compiled.GetValueOrDefault(group.Id, Array.Empty<SimpleEntry>());
                    for (; _runtime.EntryIndex < entries.Count; _runtime.EntryIndex++)
                    {
                        var entry = entries[_runtime.EntryIndex];

                        // 条件判断
                        if (!_runtime.EvaluateCondition(entry.Condition))
                            continue;

                        var handler = _registry.Resolve(entry.Type);
                        if (handler == null)
                            continue;

                        var ctx = new EntryContext { Entry = entry, Runtime = _runtime };

                        if (entry.Type == "jump")
                        {
                            handler.Start(ctx);
                            if (_runtime.IsGameEnded)
                            {
                                IsRunning = false;
                                return false;
                            }
                            // 跳转后重新开始
                            return await StepAsync(ct);
                        }

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

                    // 组执行完成，沿第一条出边转移
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
                    else // Condition
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

        return false; // 游戏结束
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

    // ── 存档 / 读档 ──

    /// <summary>创建当前状态快照。</summary>
    public GameSnapshot CreateSaveData()
    {
        return _runtime.CreateSnapshot();
    }

    /// <summary>从快照恢复状态。</summary>
    public void RestoreFrom(GameSnapshot data)
    {
        _runtime.RestoreFrom(data);
        IsRunning = true;
    }
}
