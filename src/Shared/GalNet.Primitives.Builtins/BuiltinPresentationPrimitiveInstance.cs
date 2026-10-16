using GalNet.Core.Primitives;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

/// <summary>Nonblocking presentation with state-only CatchUp and the builtin warning policy.</summary>
public abstract class BuiltinPresentationPrimitiveInstance(string? batchId, CancellationToken cancellationToken)
    : AsyncPrimitiveInstance(batchId, cancellationToken: cancellationToken)
{
    protected override void OnCatchUpDispatch() => DispatchWithoutExecution();

    protected override bool HandleExecutionFailure(Exception exception)
    {
        if (exception is not OperationCanceledException || !CancellationToken.IsCancellationRequested)
            GameLog.Logger.Warning(exception, "Primitive presentation failed");
        return true;
    }
}
