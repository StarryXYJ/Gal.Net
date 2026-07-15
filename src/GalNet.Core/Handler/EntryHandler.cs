namespace GalNet.Core.Handler;

/// <summary>
/// 条目处理器基类 —— 条目的运行时执行逻辑封装。
/// 每个 SimpleEntry Type 对应一个 EntryHandler 子类，注册到 Runtime。
///
/// 子类通过 On() 注册命令，在 Start() 中调用 Dispatch(ctx) 按参数分发。
/// </summary>
public abstract class EntryHandler
{
    private readonly Dictionary<string, Action<EntryContext>> _commands = new(StringComparer.Ordinal);

    /// <summary>对应的 SimpleEntry.Type</summary>
    public abstract string EntryType { get; }

    /// <summary>是否需要 Runtime 等待完成才推进下一句。默认 true</summary>
    public virtual bool IsBlocking => true;

    /// <summary>开始执行条目，进入 Running 状态</summary>
    public virtual void Start(EntryContext ctx) { }

    /// <summary>检查是否执行完毕，返回 true 则进入 Completed 状态</summary>
    public virtual bool IsCompleted(EntryContext ctx) => true;

    /// <summary>执行完毕后的收尾（进入 Completed 状态时调用一次）</summary>
    public virtual void Complete(EntryContext ctx) { }

    /// <summary>用户交互中断（handler 自行决定是否处理）</summary>
    public virtual void Interrupt(EntryContext ctx) { }

    /// <summary>
    /// 注册一个命令。参数 key 来自 Entry 参数中指定字段的值。
    /// </summary>
    protected void On(string command, Action<EntryContext> handler) => _commands[command] = handler;

    /// <summary>
    /// 按参数键值分发到已注册的命令。默认从 "action" 参数读取命令名。
    /// </summary>
    protected void Dispatch(EntryContext ctx, string paramKey = "action", string defaultCommand = "")
    {
        var command = ctx.GetString(paramKey, defaultCommand);
        if (_commands.TryGetValue(command, out var handler))
            handler(ctx);
    }
}
