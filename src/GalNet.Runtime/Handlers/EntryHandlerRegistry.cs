using GalNet.Core.Handler;
using GalNet.Core.Services;

namespace GalNet.Runtime.Handlers;

/// <summary>
/// Handler registration table mapping entry types to factories.
/// </summary>
public sealed class EntryHandlerRegistry
{
    private readonly Dictionary<string, Func<EntryHandler>> _handlers = new();

    /// <summary>Register a handler instance.</summary>
    public void Register(EntryHandler handler)
    {
        Register(handler.EntryType, () => handler);
    }

    /// <summary>Register a handler factory that creates a fresh instance per resolve.</summary>
    public void Register(string entryType, Func<EntryHandler> factory)
    {
        _handlers[entryType] = factory;
    }

    /// <summary>Resolve a handler for the given entry type.</summary>
    public EntryHandler? Resolve(string entryType)
    {
        return _handlers.TryGetValue(entryType, out var factory) ? factory() : null;
    }

    /// <summary>Create the default built-in handler registry.</summary>
    public static EntryHandlerRegistry CreateDefault()
    {
        var registry = new EntryHandlerRegistry();
        registry.Register("text", () => new TextHandler());
        registry.Register("audio", () => new AudioHandler());
        registry.Register("layer", () => new LayerHandler());
        registry.Register("effect", () => new EffectHandler());
        registry.Register("control", () => new ControlHandler());
        registry.Register("wait", () => new WaitHandler());
        registry.Register("variable", () => new VariableHandler());
        registry.Register("jump", () => new JumpHandler());
        registry.Register("video", () => new VideoHandler());
        return registry;
    }

    public static EntryHandlerRegistry CreateDefault(IGameProgressService? progress)
    {
        var registry = CreateDefault();
        if (progress is not null) registry.Register("unlock_gallery", () => new UnlockGalleryHandler(progress));
        return registry;
    }
}
