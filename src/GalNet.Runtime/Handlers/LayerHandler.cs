using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

public sealed class LayerHandler : EntryHandler
{
    public override string EntryType => "layer";
    public override bool IsBlocking => false;

    public LayerHandler()
    {
        On("show", ctx =>
        {
            var transition = ctx.GetString("transition", "");
            if (!string.IsNullOrEmpty(transition))
                ctx.View.ApplyTransition(transition, ctx.GetFloat("duration", 0.5f));
            ctx.View.ShowLayer(ctx.GetString("id"), ctx.GetString("asset"), ctx.GetFloat("x", 0), ctx.GetFloat("y", 0), ctx.GetFloat("z", 0));
        });
        On("hide", ctx =>
        {
            var transition = ctx.GetString("transition", "");
            if (!string.IsNullOrEmpty(transition))
                ctx.View.ApplyTransition(transition, ctx.GetFloat("duration", 0.5f));
            ctx.View.HideLayer(ctx.GetString("id"));
        });
        On("move", ctx => ctx.View.MoveLayer(ctx.GetString("id"), ctx.GetFloat("x"), ctx.GetFloat("y"), ctx.GetFloat("z", 0), ctx.GetFloat("duration", 0.5f)));
    }

    public override void Start(EntryContext ctx) => Dispatch(ctx);
}
