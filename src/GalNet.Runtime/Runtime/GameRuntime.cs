using DynamicLocalization.Core;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Variables;
using GalVariable = GalNet.Core.Variable.Variable;
using GalNet.Core.Variable;

namespace GalNet.Runtime.Runtime;

/// <summary>
/// 游戏运行时状态 —— 统一管理游戏的位置、变量、场景状态、调用栈。
/// Handler 通过 EntryContext.Runtime 访问此实例。
/// </summary>
public sealed class GameRuntime : IGameRuntime
{
    // ── 位置 ──
    public string CurrentNodeId { get; set; } = "";
    public int EntryIndex { get; set; }

    // ── 游戏结束标志 ──
    public bool IsGameEnded { get; set; }

    // ── 核心引用 ──
    public IGameView? View { get; }
    public ICultureService? I18n { get; }

    // ── 设置 ──
    public SettingsContainer Settings { get; }

    // ── 场景状态 ──
    public SceneState SceneState { get; } = new();

    // ── 内部状态 ──
    private readonly VariableStore _variables;
    private readonly ExpressionEvaluator _evaluator;
    private readonly Stack<(string NodeId, int EntryIndex)> _callStack = new();
    private readonly IVariableService? _variableService;

    public GameRuntime(IGameView? view, ICultureService? i18n, string rootNodeId = "",
        SettingsContainer? settings = null,
        IVariableService? variableService = null)
    {
        View = view;
        I18n = i18n;
        CurrentNodeId = rootNodeId;
        Settings = settings ?? new SettingsContainer();
        _variableService = variableService;

        _variables = new VariableStore(
            variableService is not null ? name => variableService.ResolveScope(name) : null,
            variableService is not null ? OnVariableChanged : null);

        if (variableService is not null)
        {
            _variables.RestorePlayerFrom(variableService.GetSnapshot(VariableScope.Player));
            _variables.RestoreSaveFrom(variableService.GetSnapshot(VariableScope.Save));
        }

        _evaluator = new ExpressionEvaluator(_variables);
    }

    private void OnVariableChanged(VariableScope scope, string name, GalVariable variable)
    {
        _variableService?.NotifyVariableChanged(scope, name, variable);
    }

    public void JumpTo(string nodeId, int entryIndex = 0)
    {
        CurrentNodeId = nodeId;
        EntryIndex = entryIndex;
    }

    public void SetEntryIndex(int entryIndex)
    {
        EntryIndex = entryIndex;
    }

    public void EndGame()
    {
        IsGameEnded = true;
    }

    // ── 变量操作 ──

    public void SetVariable(string name, object value) => _variables.Set(name, value);

    public GalVariable? GetVariable(string name)
    {
        _variables.TryGet(name, out var v);
        return v;
    }

    public bool TryGetVariable(string name, out GalVariable variable) =>
        _variables.TryGet(name, out variable!);

    public IReadOnlyDictionary<string, GalVariable> GetVariables(VariableScope scope) =>
        _variables.GetSnapshot(scope);

    public bool EvaluateCondition(string expression) => _evaluator.EvaluateCondition(expression);

    public object? EvaluateExpression(string expression) => _evaluator.Evaluate(expression);

    // ── 调用栈 ──

    public void PushCallStack(string nodeId) => _callStack.Push((nodeId, 0));

    public (string nodeId, int entryIndex)? PopCallStack()
    {
        if (_callStack.Count == 0) return null;
        var saved = _callStack.Pop();
        return saved;
    }

    // ── 存档快照 ──

    public GameSnapshot CreateSnapshot()
    {
        return new GameSnapshot
        {
            NodeId = CurrentNodeId,
            EntryIndex = EntryIndex,
            Variables = _variables.SaveSnapshot.ToDictionary(kv => kv.Key, kv => kv.Value),
            SceneState = SceneState
        };
    }

    public void RestoreFrom(GameSnapshot snapshot)
    {
        CurrentNodeId = snapshot.NodeId;
        EntryIndex = snapshot.EntryIndex;

        _variables.RestoreSaveFrom(snapshot.Variables);

        foreach (var layer in snapshot.SceneState.Layers)
            View?.ShowLayer(layer.Id, layer.AssetId, layer.X, layer.Y, layer.Z);
    }
}
