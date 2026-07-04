namespace GalNet.Core.View;

/// <summary>
/// 特效接口 —— 实时视觉效果（震动、暗角、闪白等）。
/// </summary>
public interface IEffect
{
    /// <summary>特效名称（如 "shake", "vignette", "flash"）</summary>
    string Name { get; }

    /// <summary>启动特效</summary>
    void Start(IGameView view, IReadOnlyDictionary<string, object> parameters);

    /// <summary>停止特效</summary>
    void Stop(IGameView view);
}
