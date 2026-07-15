using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 等待 —— 阻塞，等够指定秒数。
/// 参数：duration（秒）
/// </summary>
public sealed class WaitHandler : EntryHandler
{
    public override string EntryType => "wait";
    public override bool IsBlocking => true;

    private DateTime _end;

    public override void Start(EntryContext ctx)
    {
        var duration = ctx.GetFloat("duration", 1f);
        _end = DateTime.UtcNow + TimeSpan.FromSeconds(duration);
    }

    public override bool IsCompleted(EntryContext ctx) => DateTime.UtcNow >= _end;

    public override void Interrupt(EntryContext ctx)
    {
        _end = DateTime.UtcNow;
    }
}
