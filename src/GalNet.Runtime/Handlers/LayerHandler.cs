using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 图层控制 —— 非阻塞。
/// 参数：action（show/hide/move）、id、asset、x、y、z、duration
/// </summary>
public sealed class LayerHandler : EntryHandler
{
    public override string EntryType => "layer";
    public override bool IsBlocking => false;

    public override void Start(EntryContext ctx)
    {
        var action = ctx.GetString("action", "show");

        switch (action)
        {
            case "show":
                var transition = ctx.GetString("transition", "");
                if (!string.IsNullOrEmpty(transition))
                    ctx.Effects.ApplyTransition(transition, ctx.GetFloat("duration", 0.5f));

                ctx.Layers.ShowLayer(
                    ctx.GetString("id"),
                    ctx.GetString("asset"),
                    ctx.GetFloat("x", 0),
                    ctx.GetFloat("y", 0),
                    ctx.GetFloat("z", 0));
                break;
            case "hide":
                var hideTransition = ctx.GetString("transition", "");
                if (!string.IsNullOrEmpty(hideTransition))
                    ctx.Effects.ApplyTransition(hideTransition, ctx.GetFloat("duration", 0.5f));

                ctx.Layers.HideLayer(ctx.GetString("id"));
                break;
            case "move":
                ctx.Layers.MoveLayer(
                    ctx.GetString("id"),
                    ctx.GetFloat("x"),
                    ctx.GetFloat("y"),
                    ctx.GetFloat("z", 0),
                    ctx.GetFloat("duration", 0.5f));
                break;
        }
    }
}
