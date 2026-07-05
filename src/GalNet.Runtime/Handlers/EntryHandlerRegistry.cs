using GalNet.Core.Handler;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// Handler 注册表 —— EntryType → EntryHandler 映射。
/// </summary>
public sealed class EntryHandlerRegistry
{
    private readonly Dictionary<string, EntryHandler> _handlers = new();

    /// <summary>注册一个 handler。</summary>
    public void Register(EntryHandler handler)
    {
        _handlers[handler.EntryType] = handler;
    }

    /// <summary>按 entry type 查找 handler，未找到返回 null。</summary>
    public EntryHandler? Resolve(string entryType)
    {
        return _handlers.TryGetValue(entryType, out var h) ? h : null;
    }

    /// <summary>注册所有内置 handler。</summary>
    public static EntryHandlerRegistry CreateDefault()
    {
        var registry = new EntryHandlerRegistry();
        registry.Register(new TextHandler());
        registry.Register(new AudioHandler());
        registry.Register(new LayerHandler());
        registry.Register(new EffectHandler());
        registry.Register(new ControlHandler());
        registry.Register(new WaitHandler());
        registry.Register(new VariableHandler());
        registry.Register(new JumpHandler());
        registry.Register(new VideoHandler());
        return registry;
    }
}
