namespace GalNet.Core.Handler;

/// <summary>
/// 条目处理器基类 —— 条目的运行时执行逻辑封装。
/// 每个具体 Entry Type 对应一个 EntryHandler 子类，注册到 Runtime。
/// </summary>
public abstract class EntryHandler
{

    /// <summary>对应的 Entry.Type。</summary>
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

}
