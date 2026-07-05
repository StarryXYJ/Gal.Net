using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 控件控制 —— 非阻塞。
/// 参数：action（show/hide/set_property）、id
/// </summary>
public sealed class ControlHandler : EntryHandler
{
    public override string EntryType => "control";
    public override bool IsBlocking => false;

    public override void Start(EntryContext ctx)
    {
        var action = ctx.GetString("action", "show");

        switch (action)
        {
            case "show":
                ctx.View.ShowControl(ctx.GetString("id"));
                break;
            case "hide":
                ctx.View.HideControl(ctx.GetString("id"));
                break;
            case "set":
                ctx.View.SetControlProperty(
                    ctx.GetString("id"),
                    ctx.GetString("property"),
                    ctx.GetString("value"));
                break;
        }
    }
}
