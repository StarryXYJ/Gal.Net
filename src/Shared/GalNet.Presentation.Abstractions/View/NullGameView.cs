using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;

namespace GalNet.Presentation.Abstractions.View;

/// <summary>An empty view with no registered contracts or executable instructions.</summary>
public sealed class NullGameView : IGameView
{
    public IReadOnlyCollection<EntryDefinition> Primitives => [];
    public bool CanExecute(string primitiveType) => false;
    public bool TryGetEntry(string primitiveType, out EntryDefinition? entry)
    {
        entry = null;
        return false;
    }
    public PrimitiveInstance Dispatch(PrimitiveEntry entry, IGameRuntime runtime, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"Instruction '{entry.Type}' has no registered implementation.");
    public void Dispose() => GC.SuppressFinalize(this);
}
