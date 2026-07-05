using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 文本显示 —— 打字机效果，阻塞等待用户点击。
/// 参数：speaker（说话人）、content（I18nKey）、voice（可选语音）
/// </summary>
public sealed class TextHandler : EntryHandler
{
    public override string EntryType => "text";
    public override bool IsBlocking => true;

    private bool _completed;

    public override void Start(EntryContext ctx)
    {
        _completed = false;

        var speaker = ctx.GetText("speaker");
        var content = ctx.GetText("content");
        var voice = ctx.GetString("voice");
        var widgetId = ctx.GetString("widget", "default_dialogue");

        if (!string.IsNullOrEmpty(voice))
            ctx.Text.SetVoice(voice);

        _ = ctx.Text.StartTypewriter(widgetId, speaker, content, CancellationToken.None);
    }

    public override bool IsCompleted(EntryContext ctx) => _completed;

    public override void Complete(EntryContext ctx)
    {
        _completed = true;
    }

    public override void Interrupt(EntryContext ctx)
    {
        var widgetId = ctx.GetString("widget", "default_dialogue");
        ctx.Text.SkipTypewriter(widgetId);
        _completed = true;
    }
}
