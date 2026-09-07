namespace GalNet.Runtime.Handlers;

/// <summary>
/// 等待 —— 阻塞，等够指定秒数。
/// 参数：duration（秒）
/// </summary>
public sealed class WaitHandler : EntryHandler
{
    public override string EntryType => "wait";

    public override Task ExecuteAsync(EntryContext context, GalNet.Core.View.IGameView view, TimeProvider timeProvider, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Max(0, context.GetFloat("duration", 1f))), timeProvider, ct);
}
