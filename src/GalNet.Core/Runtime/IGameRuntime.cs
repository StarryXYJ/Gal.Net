using DynamicLocalization.Core;
using GalNet.Core.Scene;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Core.View;

namespace GalNet.Core.Runtime;

/// <summary>
/// 游戏运行时状态接口 —— 统一管理游戏当前的位置、变量、场景状态等。
/// 由 GameRuntime 实现，通过 EntryContext 注入 Handler。
/// </summary>
public interface IGameRuntime
{
    // ── 位置 ──
    string CurrentNodeId { get; set; }
    int EntryIndex { get; set; }

    // ── 游戏结束标志 ──
    bool IsGameEnded { get; set; }

    // ── 核心引用 ──
    IGameView? View { get; }
    ICultureService? I18n { get; }

    // ── 设置 ──
    SettingsContainer Settings { get; }

    // ── 场景状态 ──
    SceneState SceneState { get; }

    // ── 变量操作 ──
    void SetVariable(VariableRoute route, object value);
    GalNet.Core.Variable.Variable? GetVariable(VariableRoute route);
    bool TryGetVariable(VariableRoute route, out GalNet.Core.Variable.Variable variable);
    bool EvaluateCondition(string expression);
    object? EvaluateExpression(string expression);

    // ── 调用栈（call/return） ──
    void PushCallStack(string nodeId);
    (string nodeId, int entryIndex)? PopCallStack();

    // ── 存档快照 ──
    GameSnapshot CreateSnapshot();
    void RestoreFrom(GameSnapshot snapshot);
}
