using GalNet.Core.Scene;

namespace GalNet.Core.Runtime;

/// <summary>
/// 游戏运行时快照 —— 用于存档/读档的不可变数据对象。
/// </summary>
public sealed class GameSnapshot
{
    public string NodeId { get; init; } = "";
    public int EntryIndex { get; init; }
    public Dictionary<string, GalNet.Core.Variable.Variable> Variables { get; init; } = [];
    public SceneState SceneState { get; init; } = new();
}
