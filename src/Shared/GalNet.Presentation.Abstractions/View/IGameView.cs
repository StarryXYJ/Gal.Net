using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;

namespace GalNet.Presentation.Abstractions.View;

/// <summary>Resolves and dispatches one compiled primitive call.</summary>
public interface IGameView : IDisposable, IAsyncDisposable
{
    IReadOnlyCollection<EntryDefinition> Primitives { get; }

    bool TryGetEntry(string primitiveType, out EntryDefinition? entry);

    bool CanExecute(string primitiveType);

    PrimitiveInstance Dispatch(
        PrimitiveEntry entry,
        IGameRuntime runtime,
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
