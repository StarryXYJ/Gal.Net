namespace GalNet.Core.Scene;

/// <summary>
/// 场景快照 —— 存档中的画面状态。
/// </summary>
public sealed class SceneState
{
    /// <summary>当前活跃的 Layer 列表</summary>
    public List<Layer> Layers { get; init; } = [];

    /// <summary>当前活跃的控件实例 ID 列表</summary>
    public List<string> ActiveControlIds { get; init; } = [];

    /// <summary>当前活跃的特效实例 ID 列表</summary>
    public List<string> ActiveEffectIds { get; init; } = [];

    /// <summary>Replayable state for effects that remain active across a stable save.</summary>
    public List<ActiveEffectState> ActiveEffects { get; init; } = [];

    /// <summary>Looping primitive animations that are active but do not prevent a stable save.</summary>
    public List<ActiveAnimationState> ActiveAnimations { get; init; } = [];

    /// <summary>当前活跃的转场名称（null = 无）</summary>
    public string? ActiveTransition { get; set; }
}
