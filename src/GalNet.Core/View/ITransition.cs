namespace GalNet.Core.View;

/// <summary>
/// 转场接口 —— 场景切换时的过渡动画。
/// </summary>
public interface ITransition
{
    /// <summary>转场名称（如 "fade", "slide_left"）</summary>
    string Name { get; }

    /// <summary>执行转场动画</summary>
    Task ExecuteAsync(IGameView view, string? fromAsset, string? toAsset,
                      float durationSec, CancellationToken ct);
}
