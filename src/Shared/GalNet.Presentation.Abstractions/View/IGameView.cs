using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;

namespace GalNet.Presentation.Abstractions.View;

/// <summary>Resolves and dispatches one compiled primitive call.</summary>
public interface IGameView : IDisposable
{
    IReadOnlyCollection<PrimitiveEntryBase> Primitives { get; }

    bool TryGetEntry(string primitiveType, out PrimitiveEntryBase? entry);

    PrimitiveInstance? Dispatch(
        PrimitiveEntry entry,
        IGameRuntime runtime,
        CancellationToken cancellationToken);

    void IDisposable.Dispose() { }
}
