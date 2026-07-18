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
        registry.Register(new TextHandler());
        registry.Register(new ShowLayerHandler()); registry.Register(new HideLayerHandler()); registry.Register(new MoveLayerHandler());
        registry.Register(new PlayAudioHandler()); registry.Register(new StopAudioHandler()); registry.Register(new PauseAudioHandler()); registry.Register(new ResumeAudioHandler()); registry.Register(new EnqueueAudioHandler());
        registry.Register(new PlayVideoHandler()); registry.Register(new StopVideoHandler());
        registry.Register(new ShowControlHandler()); registry.Register(new HideControlHandler()); registry.Register(new SetControlHandler());
        registry.Register(new ApplyEffectHandler()); registry.Register(new StopEffectHandler());
        registry.Register(new WaitHandler()); registry.Register(new SetVariableHandler()); registry.Register(new EvaluateVariableHandler());
        return registry;
    }

    public static EntryHandlerRegistry CreateDefault(IGameProgressService? progress)
    {
        var registry = CreateDefault();
        if (progress is not null) registry.Register("unlock_gallery", () => new UnlockGalleryHandler(progress));
        return registry;
    }
}
