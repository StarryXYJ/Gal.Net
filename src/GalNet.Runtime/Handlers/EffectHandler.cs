using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 特效控制 —— 非阻塞。
/// 参数：action（apply/stop）、type、duration、params
/// </summary>
public sealed class EffectHandler : EntryHandler
{
    public override string EntryType => "effect";
    public override bool IsBlocking => false;

    public override void Start(EntryContext ctx)
    {
        var action = ctx.GetString("action", "apply");

        switch (action)
        {
            case "apply":
                var parameters = new Dictionary<string, object>();
                foreach (var (key, value) in ctx.Params)
                {
                    if (key is "action" or "type") continue;
                    parameters[key] = value;
                }
                ctx.View.ApplyEffect(ctx.GetString("type"), parameters);
                break;
            case "stop":
                ctx.View.StopEffect(ctx.GetString("id"));
                break;
        }
    }
}
