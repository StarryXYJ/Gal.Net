using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

public sealed class JumpHandler : EntryHandler
{
    public override string EntryType => "jump";
    public override bool IsBlocking => false;

    public JumpHandler()
    {
        On("goto", ctx => ctx.Runtime.JumpTo(ctx.GetString("target", "")));
        On("end", ctx => ctx.Runtime.EndGame());
        On("call", ctx =>
        {
            ctx.Runtime.PushCallStack(ctx.Runtime.CurrentNodeId);
            ctx.Runtime.JumpTo(ctx.GetString("target", ""));
        });
        On("return", ctx =>
        {
            var saved = ctx.Runtime.PopCallStack();
            if (saved.HasValue)
                ctx.Runtime.JumpTo(saved.Value.nodeId, saved.Value.entryIndex);
        });
    }

    public override void Start(EntryContext ctx) => Dispatch(ctx, paramKey: "type", defaultCommand: "goto");
}
