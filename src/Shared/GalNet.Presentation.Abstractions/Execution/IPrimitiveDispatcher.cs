using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;

namespace GalNet.Presentation.Abstractions.Execution;

/// <summary>Resolves and dispatches one compiled primitive call.</summary>
public interface IPrimitiveDispatcher : IDisposable, IAsyncDisposable
{
    StateCatalog StateCatalog => StateCatalog.Empty;
    bool RequiresStateRestore => false;
    void ValidateState(IGameContext runtime, GameState state) { }
    IReadOnlyCollection<EntryDefinition> Primitives { get; }

    bool TryGetEntry(string primitiveType, out EntryDefinition? entry);

    bool CanExecute(string primitiveType);

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
