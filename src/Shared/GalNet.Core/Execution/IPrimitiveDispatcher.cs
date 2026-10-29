using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;

namespace GalNet.Core.Execution;

/// <summary>Resolves and dispatches one compiled primitive call.</summary>
public interface IPrimitiveDispatcher : IDisposable, IAsyncDisposable
{
    StateCatalog StateCatalog => StateCatalog.Empty;
    bool RequiresStateRestore => false;
    void ValidateState(IGameContext runtime, GameState state) { }
    IReadOnlyCollection<EntryDefinition> Primitives { get; }

    bool TryGetEntry(string primitiveType, out EntryDefinition? entry);

    bool CanExecute(string primitiveType);

    /// <summary>Notifies optional host observers when an instruction's condition excludes its execution.</summary>
    void OnInstructionSkipped(PrimitiveEntry entry, IGameContext runtime) { }

    /// <summary>Stops transient host interaction before an accepted story advance or playback-mode continuation.</summary>
    void OnPlaybackContinuing() { }

    /// <summary>Commits completed host observations under the execution gate before capturing persistent state.</summary>
    void OnCheckpointCreating(IGameContext runtime) { }

    PrimitiveInstance Dispatch(
        PrimitiveEntry entry,
        IGameContext runtime,
        CancellationToken cancellationToken,
        PrimitiveInvocationMode mode = PrimitiveInvocationMode.Normal);

    void IDisposable.Dispose() => GC.SuppressFinalize(this);
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
