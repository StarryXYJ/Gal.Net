using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

public sealed class VideoHandler : EntryHandler
{
    public override string EntryType => "video";
    public override bool IsBlocking => false;

    public VideoHandler()
    {
        On("play", ctx => ctx.View.PlayVideo(ctx.GetString("asset")));
        On("stop", ctx => ctx.View.StopVideo());
    }

    public override void Start(EntryContext ctx) => Dispatch(ctx);
}
