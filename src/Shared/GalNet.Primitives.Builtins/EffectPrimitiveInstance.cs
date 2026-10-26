using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class ApplyEffectPrimitiveInstance(IGameContext runtime, IEffectPresenter? presenter,
    EffectRequest request, string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    public ApplyEffectPrimitiveInstance(IEffectPresenter? presenter, PrimitiveCreateContext context, EffectApplyOptions options)
        : this(context.Runtime, presenter, BuiltinRuntimeActions.CreateEffectRequest(options), context.BatchId, context.ScopeCancellation) { }

    protected override void OnStarting() => BuiltinRuntimeActions.ApplyEffectState(runtime, request);
    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.StartEffectAsync(request, token) ?? Task.CompletedTask;
}

public sealed class StopEffectPrimitiveInstance(IGameContext runtime, IEffectPresenter? presenter,
    string instanceId, string? batchId, CancellationToken scopeCancellation)
    : BuiltinPresentationPrimitiveInstance(batchId, scopeCancellation)
{
    public StopEffectPrimitiveInstance(IEffectPresenter? presenter, PrimitiveCreateContext context, InstanceIdOptions options)
        : this(context.Runtime, presenter, options.InstanceId ?? "", context.BatchId, context.ScopeCancellation) { }

    protected override void OnStarting() => BuiltinRuntimeActions.RemoveEffectState(runtime, instanceId);
    protected override Task ExecuteAsync(CancellationToken token) =>
        presenter?.StopEffectAsync(instanceId, token) ?? Task.CompletedTask;
}
