using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

public sealed class ApplyEffectPrimitiveInstance : PrimitiveInstance
{
    private Task _presentationTask = Task.CompletedTask;
    public override Task PresentationCompletion => _presentationTask;
    private readonly IGameRuntime _runtime;
    private readonly IEffectPresenter? _presenter;
    private readonly EffectRequest _request;
    private readonly CancellationToken _scopeCancellation;

    public ApplyEffectPrimitiveInstance(
        IGameRuntime runtime,
        IEffectPresenter? presenter,
        EffectRequest request,
        string? batchId,
        CancellationToken scopeCancellation)
        : base(batchId)
    {
        _runtime = runtime;
        _presenter = presenter;
        _request = request;
        _scopeCancellation = scopeCancellation;
    }

    public override bool IsBlocking => false;
    public override bool IsSkippable => false;

    protected override void OnDispatch()
    {
        BuiltinRuntimeActions.ApplyEffectState(_runtime, _request);
        if (_presenter is null || InvocationMode == PrimitiveInvocationMode.CatchUp)
        {
            TryComplete();
            return;
        }
        _presentationTask = ObservePresentationAsync();
    }

    private async Task ObservePresentationAsync()
    {
        try
        {
            await _presenter!.StartEffectAsync(_request, _scopeCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested)
        { }
        catch (Exception error) { GameLog.Logger.Warning(error, "Primitive presentation failed"); }
        finally
        {
            TryComplete();
        }
    }
}

public sealed class StopEffectPrimitiveInstance : PrimitiveInstance
{
    private Task _presentationTask = Task.CompletedTask;
    public override Task PresentationCompletion => _presentationTask;
    private readonly IGameRuntime _runtime;
    private readonly IEffectPresenter? _presenter;
    private readonly string _instanceId;
    private readonly CancellationToken _scopeCancellation;

    public StopEffectPrimitiveInstance(
        IGameRuntime runtime,
        IEffectPresenter? presenter,
        string instanceId,
        string? batchId,
        CancellationToken scopeCancellation)
        : base(batchId)
    {
        _runtime = runtime;
        _presenter = presenter;
        _instanceId = instanceId;
        _scopeCancellation = scopeCancellation;
    }

    public override bool IsBlocking => false;
    public override bool IsSkippable => false;

    protected override void OnDispatch()
    {
        BuiltinRuntimeActions.RemoveEffectState(_runtime, _instanceId);
        if (_presenter is null || InvocationMode == PrimitiveInvocationMode.CatchUp)
        {
            TryComplete();
            return;
        }
        _presentationTask = ObservePresentationAsync();
    }

    private async Task ObservePresentationAsync()
    {
        try
        {
            await _presenter!.StopEffectAsync(_instanceId, _scopeCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested)
        { }
        catch (Exception error) { GameLog.Logger.Warning(error, "Primitive presentation failed"); }
        finally
        {
            TryComplete();
        }
    }
}
