using GalNet.Core.Primitives;

namespace GalNet.Core.View;

/// <summary>
/// Dynamic Game Scope facade. It exposes no rendering, media, or interaction
/// capabilities: every operation is discovered by its primitive type ID.
/// </summary>
public interface IGameView : IDisposable
{
    /// <summary>Descriptors exposed by the Game Scope's frozen primitive modules.</summary>
    IReadOnlyCollection<PrimitiveDescriptor> Primitives { get; }

    /// <summary>Looks up metadata without starting primitive execution.</summary>
    bool TryGetDescriptor(string primitiveType, out PrimitiveDescriptor? descriptor);

    /// <summary>Routes a generic primitive invocation to its Game Scope module.</summary>
    PrimitiveDispatch Dispatch(
        PrimitiveInvocation invocation,
        PrimitiveExecutionControl control,
        CancellationToken cancellationToken);

    void IDisposable.Dispose() { }
}
