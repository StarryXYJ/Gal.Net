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
                ctx.Audio.PlayAudio(
                    ctx.GetString("channel", "bgm"),
                    ctx.GetString("asset"),
                    ctx.GetFloat("volume", 0.8f),
                    ctx.GetString("mode", "once"),
                    ctx.GetInt("times", 1));
                break;
            case "stop":
                ctx.Audio.StopAudio(ctx.GetString("channel"));
                break;
            case "pause":
                ctx.Audio.PauseAudio(ctx.GetString("channel"));
                break;
            case "resume":
                ctx.Audio.ResumeAudio(ctx.GetString("channel"));
                break;
            case "enqueue":
                ctx.Audio.EnqueueAudio(
                    ctx.GetString("channel"),
                    ctx.GetString("asset"),
                    ctx.GetInt("times", 1));
                break;
        }
    }
}
