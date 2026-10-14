using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class ApplyEffectPrimitiveInstance(IGameRuntime runtime, IEffectPresenter? presenter,
    EffectRequest request, string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    protected override void OnStarting() => BuiltinRuntimeActions.ApplyEffectState(runtime, request);
    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.StartEffectAsync(request, token) ?? Task.CompletedTask;
}

public sealed class StopEffectPrimitiveInstance(IGameRuntime runtime, IEffectPresenter? presenter,
    string instanceId, string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    protected override void OnStarting() => BuiltinRuntimeActions.RemoveEffectState(runtime, instanceId);
    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.StopEffectAsync(instanceId, token) ?? Task.CompletedTask;
}
