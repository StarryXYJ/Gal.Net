using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

public sealed class EffectHandler : EntryHandler
{
    public override string EntryType => "effect";
    public override bool IsBlocking => false;

    public EffectHandler()
    {
        On("apply", ctx =>
        {
            var parameters = new Dictionary<string, object>();
            foreach (var (key, value) in ctx.Params)
            {
                if (key is "action" or "type") continue;
                parameters[key] = value;
            }
            ctx.View.ApplyEffect(ctx.GetString("type"), parameters);
        });
        On("stop", ctx => ctx.View.StopEffect(ctx.GetString("id")));
    }

    public override void Start(EntryContext ctx) => Dispatch(ctx);
}
