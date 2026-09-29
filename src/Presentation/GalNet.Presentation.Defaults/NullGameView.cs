using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Presentation.Defaults;

/// <summary>A headless view with no mounted primitive capabilities.</summary>
public class NullGameView : IGameView
{
    public IReadOnlyCollection<PrimitiveEntryBase> Primitives => [];
    public bool TryGetEntry(string primitiveType, out PrimitiveEntryBase? entry)
    {
        entry = null;
        return false;
    }
    public PrimitiveInstance? Dispatch(PrimitiveEntry entry, IGameRuntime runtime, CancellationToken cancellationToken) => null;
    public virtual void Dispose() { }
}
