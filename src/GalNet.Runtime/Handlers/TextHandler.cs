using GalNet.Core.View;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 文本显示 —— 打字机效果，阻塞等待用户点击。
/// 参数：speaker（说话人）、content（I18nKey）、voice（可选语音）
/// </summary>
public sealed class TextHandler : EntryHandler
{
    public override string EntryType => "text";
    public override bool CreatesCheckpoint => true;

    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var speaker = context.GetText("speaker");
        var content = context.GetText("content");
        var voice = context.GetString("voice");
        const string widgetId = "default_dialogue";

        if (!string.IsNullOrEmpty(voice))
            view.SetVoice(voice);

        var typewriter = view.StartTypewriter(widgetId, speaker, content, ct);
        await view.WaitForClickAsync(ct);
        view.SkipTypewriter(widgetId);
        await typewriter;
    }
}
