using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 音频控制 —— 非阻塞。
/// 参数：action（play/stop/pause/resume/enqueue）、channel、asset、volume、mode、times
/// </summary>
public sealed class AudioHandler : EntryHandler
{
    public override string EntryType => "audio";
    public override bool IsBlocking => false;

    public override void Start(EntryContext ctx)
    {
        var action = ctx.GetString("action", "play");

        switch (action)
        {
            case "play":
                ctx.View.PlayAudio(
                    ctx.GetString("channel", "bgm"),
                    ctx.GetString("asset"),
                    ctx.GetFloat("volume", 0.8f),
                    ctx.GetString("mode", "once"),
                    ctx.GetInt("times", 1));
                break;
            case "stop":
                ctx.View.StopAudio(ctx.GetString("channel"));
                break;
            case "pause":
                ctx.View.PauseAudio(ctx.GetString("channel"));
                break;
            case "resume":
                ctx.View.ResumeAudio(ctx.GetString("channel"));
                break;
            case "enqueue":
                ctx.View.EnqueueAudio(
                    ctx.GetString("channel"),
                    ctx.GetString("asset"),
                    ctx.GetInt("times", 1));
                break;
        }
    }
}
