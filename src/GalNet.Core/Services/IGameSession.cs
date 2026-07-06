namespace GalNet.Core.Services;

/// <summary>
/// 游戏会话 —— 封装"加载图 → 启动引擎 → 运行到结束"全流程。
/// 与 INavigationService 解耦：ViewModel 只依赖此接口，不直接接触 GameEngine。
/// </summary>
public interface IGameSession
{
    /// <summary>
    /// 异步运行游戏，直到引擎结束或取消。
    /// </summary>
    /// <param name="onEnded">游戏正常结束时的回调（在 UI 线程调用）。</param>
    /// <param name="ct">取消令牌。</param>
    Task RunAsync(Action? onEnded = null, CancellationToken ct = default);
}
