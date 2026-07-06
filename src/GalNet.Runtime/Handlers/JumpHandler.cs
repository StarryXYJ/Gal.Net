using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// 跳转控制 —— 非阻塞。
/// 参数：type（goto/end/call/return）、target（目标节点 ID）
/// 跳转逻辑通过 Runtime 修改 CurrentNodeId / 调用栈实现，
/// GameEngine 在 handler 执行后检测节点变化并做控制流转。
/// </summary>
public sealed class JumpHandler : EntryHandler
{
    public override string EntryType => "jump";
    public override bool IsBlocking => false;

    public override void Start(EntryContext ctx)
    {
        var jumpType = ctx.GetString("type", "goto");
        var target = ctx.GetString("target", "");
        var runtime = ctx.Runtime;

        switch (jumpType)
        {
            case "goto":
                runtime.JumpTo(target);
                break;

            case "end":
                runtime.EndGame();
                break;

            case "call":
                runtime.PushCallStack(runtime.CurrentNodeId);
                runtime.JumpTo(target);
                break;

            case "return":
            {
                var saved = runtime.PopCallStack();
                if (saved.HasValue)
                {
                    runtime.JumpTo(saved.Value.nodeId, saved.Value.entryIndex);
                }
                break;
            }
        }
    }
}
