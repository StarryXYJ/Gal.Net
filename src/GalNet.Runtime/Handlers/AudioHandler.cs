using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

public sealed class AudioHandler : EntryHandler
{
    public override string EntryType => "audio";
    public override bool IsBlocking => false;

    public AudioHandler()
    {
        On("play", ctx => ctx.View.PlayAudio(ctx.GetString("channel", "bgm"), ctx.GetString("asset"), ctx.GetFloat("volume", 0.8f), ctx.GetString("mode", "once"), ctx.GetInt("times", 1)));
        On("stop", ctx => ctx.View.StopAudio(ctx.GetString("channel")));
        On("pause", ctx => ctx.View.PauseAudio(ctx.GetString("channel")));
        On("resume", ctx => ctx.View.ResumeAudio(ctx.GetString("channel")));
        On("enqueue", ctx => ctx.View.EnqueueAudio(ctx.GetString("channel"), ctx.GetString("asset"), ctx.GetInt("times", 1)));
    }

    public override void Start(EntryContext ctx) => Dispatch(ctx);
}
