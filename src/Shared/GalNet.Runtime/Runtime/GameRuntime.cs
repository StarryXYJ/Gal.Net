using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Services;
using GalNet.Core.Variable;
using GalNet.Runtime.Variables;
using GalNet.Runtime.Logging;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Runtime.Runtime;

/// <summary>
/// 游戏运行时状态 —— 统一管理游戏的位置、变量和场景状态。
/// Primitive module 通过 PrimitiveCreateContext.Runtime 访问此实例。
/// </summary>
public sealed class GameRuntime : IGameRuntime
{
    // ── 位置 ──
    public string CurrentNodeId { get; private set; } = "";
    public int EntryIndex { get; private set; }

    // ── 游戏结束标志 ──
    public bool IsGameEnded { get; private set; }

    // ── 核心引用 ──
    public ITextResolver TextResolver { get; }

    // ── 场景状态 ──
    public GameState State { get; private set; } = new();
    public ISceneInstanceManager SceneInstances { get; private set; }

    // ── 内部状态 ──
    private readonly VariableStore _variables;
    private readonly ExpressionEvaluator _evaluator;
    private readonly IVariableService? _variableService;

    public GameRuntime(ITextResolver? textResolver, string rootNodeId = "",
        IVariableService? variableService = null, StateCatalog? stateCatalog = null)
    {
        TextResolver = textResolver ?? PassthroughTextResolver.Instance;
        CurrentNodeId = rootNodeId;
        State = new GameState(stateCatalog);
        SceneInstances = new SceneInstanceManager(State);
        _variableService = variableService;

        _variables = new VariableStore(
            variableService is not null ? name => variableService.ResolveScope(name) : null,
            variableService is not null ? OnVariableChanged : null);

        if (variableService is not null)
        {
            _variables.RestorePlayerFrom(variableService.GetSnapshot(VariableScope.Player));
            _variables.RestoreSaveFrom(variableService.GetSnapshot(VariableScope.Save));
        }

        _evaluator = new ExpressionEvaluator(_variables, (expression, error) => GameLog.Logger.Warning(error,
            "{DiagnosticCode} at group {GroupId}, entry index {EntryIndex}: {Expression}",
            "expression.invalid", CurrentNodeId, EntryIndex, expression));
    }

    private void OnVariableChanged(VariableScope scope, string name, GalVariable variable)
    {
        if (_variableService is null) return;
        // The local store strips scope prefixes. Preserve the bridge's existing key
        // so editing a restored system/player variable cannot create a second alias.
        var originalName = _variableService.GetSnapshot(scope).FirstOrDefault(pair => pair.Value.Uid == variable.Uid).Key;
        _variableService.NotifyVariableChanged(scope, originalName ?? name, variable);
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

    public void SetVariable(string name, object value) => State.Update(() => _variables.Set(name, value));

    public GalVariable? GetVariable(string name)
    {
        return State.Read(() => _variables.TryGet(name, out var value) ? value : null);
    }

    public bool TryGetVariable(string name, out GalVariable variable)
    {
        variable = GetVariable(name)!;
        return variable is not null;
    }

    public IReadOnlyDictionary<string, GalVariable> GetVariables(VariableScope scope) =>
        State.Read(() => _variables.GetSnapshot(scope));

    public bool EvaluateCondition(string expression) => _evaluator.EvaluateCondition(expression);

    public object? EvaluateExpression(string expression) => _evaluator.Evaluate(expression);

    // ── 存档快照 ──

    public GameSnapshot CreateSnapshot() => State.Read(() => new GameSnapshot
    {
        Version = GameSnapshot.CurrentFormatVersion,
        NodeId = CurrentNodeId,
        EntryIndex = EntryIndex,
        Variables = _variables.SaveSnapshot.ToDictionary(pair => pair.Key, pair => pair.Value),
        States = State.Capture()
    });

    public GameRestorePoint PrepareRestore(GameSnapshot snapshot) => GameRestorePoint.Prepare(State.Catalog, snapshot);

    public void RestoreFrom(GameSnapshot snapshot) => Restore(PrepareRestore(snapshot));

    public void Restore(GameRestorePoint point)
    {
        State.Update(() =>
        {
            CurrentNodeId = point.NodeId;
            EntryIndex = point.EntryIndex;
            _variables.RestoreSaveFrom(point.Variables);
            IsGameEnded = false;
            State = point.State;
            SceneInstances = new SceneInstanceManager(State);
        });
    }
}
