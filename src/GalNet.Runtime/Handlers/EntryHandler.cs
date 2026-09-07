using GalNet.Core.View;

namespace GalNet.Runtime.Handlers;

/// <summary>Executes one entry against explicit runtime and presentation dependencies.</summary>
public abstract class EntryHandler
{
    public abstract string EntryType { get; }

    /// <summary>Whether the engine creates a save checkpoint before this entry waits for user input.</summary>
    public virtual bool CreatesCheckpoint => false;

    public abstract Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct);
}
