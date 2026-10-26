using GalNet.Core.Runtime;

namespace GalNet.Core.Execution;

/// <summary>Host presentation suspension and final-state synchronization for forward catch-up.</summary>
public interface ICatchUpPresentation
{
    Task BeginCatchUpAsync(CancellationToken cancellationToken);
    Task EndCatchUpAsync(IGameContext runtime, CancellationToken cancellationToken);
}
