using DynamicLocalization.Core;
using GalNet.Core.Scene;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Core.View;

namespace GalNet.Core.Runtime;

/// <summary>
/// 游戏运行时状态接口 —— 统一管理游戏当前的位置、变量、场景状态等。
/// 由 GameRuntime 实现，通过 EntryContext 注入 Handler。
///
/// 设计原则：
///   - 位置/结束标志仅暴露只读属性，防止 Handler 意外修改控制流。
///   - 所有控制流变更通过显式方法（JumpTo / EndGame）进行，意图明确。
/// </summary>
public interface IGameRuntime
{
    // ── 位置（只读）──
    string CurrentNodeId { get; }
    int EntryIndex { get; }

    // ── 游戏结束标志（只读）──
    bool IsGameEnded { get; }

    // ── 核心引用 ──
    IGameView? View { get; }
    ICultureService? I18n { get; }

    // ── 设置 ──
    SettingsContainer Settings { get; }

    // ── 场景状态 ──
    SceneState SceneState { get; }

    // ── 控制流方法 ──

    /// <summary>跳转到指定节点，并重置 EntryIndex。</summary>
    void JumpTo(string nodeId, int entryIndex = 0);

    /// <summary>直接设置条目索引进度（引擎步进或测试用）。</summary>
    void SetEntryIndex(int entryIndex);

    /// <summary>标记游戏结束。</summary>
    void EndGame();

    // ── 调用栈（call/return）──
    void PushCallStack(string nodeId);
    (string nodeId, int entryIndex)? PopCallStack();

    // ── 变量操作 ──
    void SetVariable(VariableRoute route, object value);
    GalNet.Core.Variable.Variable? GetVariable(VariableRoute route);
    bool TryGetVariable(VariableRoute route, out GalNet.Core.Variable.Variable variable);
    bool EvaluateCondition(string expression);
    object? EvaluateExpression(string expression);

    // ── 存档快照 ──
    GameSnapshot CreateSnapshot();
    void RestoreFrom(GameSnapshot snapshot);
}
