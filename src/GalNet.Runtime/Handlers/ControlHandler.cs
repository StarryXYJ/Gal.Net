using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

public sealed class ControlHandler : EntryHandler
{
    public override string EntryType => "control";
    public override bool IsBlocking => false;

    public ControlHandler()
    {
        On("show", ctx => ctx.View.ShowControl(ctx.GetString("id")));
        On("hide", ctx => ctx.View.HideControl(ctx.GetString("id")));
        On("set", ctx => ctx.View.SetControlProperty(ctx.GetString("id"), ctx.GetString("property"), ctx.GetString("value")));
    }

    public override void Start(EntryContext ctx) => Dispatch(ctx);
}
