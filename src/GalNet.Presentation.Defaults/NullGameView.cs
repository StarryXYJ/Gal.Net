using GalNet.Core.View;
using GalNet.Core.Primitives;

namespace GalNet.Presentation.Defaults;

/// <summary>Framework-neutral no-op game view for tests and headless hosts.</summary>
public class NullGameView : IGameView
{
    private static readonly PrimitiveDispatch Skipped = new(
        PrimitiveDispatchStatus.Skipped,
        new PrimitiveExecutionPolicy(false, false, null),
        Task.FromResult(PrimitiveResult.Empty));

    public IReadOnlyCollection<PrimitiveDescriptor> Primitives { get; } = [];
    public virtual bool TryGetDescriptor(string primitiveType, out PrimitiveDescriptor? descriptor)
    {
        descriptor = null;
        return false;
    }
    public virtual PrimitiveDispatch Dispatch(PrimitiveInvocation invocation, PrimitiveExecutionControl control, CancellationToken cancellationToken) => Skipped;

}
