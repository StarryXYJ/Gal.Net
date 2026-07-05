using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 视频播放 —— 非阻塞。
/// 参数：action（play/stop）、asset
/// </summary>
public sealed class VideoHandler : EntryHandler
{
    public override string EntryType => "video";
    public override bool IsBlocking => false;

    public override void Start(EntryContext ctx)
    {
        var action = ctx.GetString("action", "play");

        switch (action)
        {
            case "play":
                ctx.Video.PlayVideo(ctx.GetString("asset"));
                break;
            case "stop":
                ctx.Video.StopVideo();
                break;
        }
    }
}
