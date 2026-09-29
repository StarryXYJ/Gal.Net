using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.View;

namespace GalNet.Primitives.Builtins;

public sealed class PlayParticlePrimitiveInstance : PrimitiveInstance
{
    private readonly IGameRuntime _runtime;
    private readonly IParticlePresenter? _presenter;
    private readonly ParticleEmitterRequest _request;
    private readonly CancellationToken _scopeCancellation;

    public PlayParticlePrimitiveInstance(IGameRuntime runtime, IParticlePresenter? presenter, ParticleEmitterRequest request, string? batchId, CancellationToken scopeCancellation) : base(batchId)
    { _runtime = runtime; _presenter = presenter; _request = request; _scopeCancellation = scopeCancellation; }
    public override bool IsBlocking => false;
    public override bool IsSkippable => false;
    protected override void OnDispatch()
    {
        BuiltinRuntimeActions.ApplyParticleState(_runtime, _request);
        if (_presenter is null) { TryComplete(); return; }
        _ = ObserveAsync();
    }
    private async Task ObserveAsync()
    {
        try { await _presenter!.StartParticleEmitterAsync(_request, _scopeCancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested) { }
        catch { }
        finally { TryComplete(); }
    }
}

public sealed class StopParticlePrimitiveInstance : PrimitiveInstance
{
    private readonly IGameRuntime _runtime;
    private readonly IParticlePresenter? _presenter;
    private readonly string _instanceId;
    private readonly CancellationToken _scopeCancellation;
    public StopParticlePrimitiveInstance(IGameRuntime runtime, IParticlePresenter? presenter, string instanceId, string? batchId, CancellationToken scopeCancellation) : base(batchId)
    { _runtime = runtime; _presenter = presenter; _instanceId = instanceId; _scopeCancellation = scopeCancellation; }
    public override bool IsBlocking => false;
    public override bool IsSkippable => false;
    protected override void OnDispatch()
    {
        BuiltinRuntimeActions.RemoveParticleState(_runtime, _instanceId);
        if (_presenter is null) { TryComplete(); return; }
        _ = ObserveAsync();
    }
    private async Task ObserveAsync()
    {
        try { await _presenter!.StopParticleEmitterAsync(_instanceId, _scopeCancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested) { }
        catch { }
        finally { TryComplete(); }
    }
}

public sealed class BurstParticlePrimitiveInstance : PrimitiveInstance
{
    private readonly IParticlePresenter? _presenter;
    private readonly ParticleBurstRequest _request;
    private readonly CancellationToken _scopeCancellation;

    public BurstParticlePrimitiveInstance(IParticlePresenter? presenter, ParticleBurstRequest request, string? batchId, CancellationToken scopeCancellation) : base(batchId)
    { _presenter = presenter; _request = request; _scopeCancellation = scopeCancellation; }
    public override bool IsBlocking => false;
    public override bool IsSkippable => false;
    protected override void OnDispatch()
    {
        if (_presenter is null) { TryComplete(); return; }
        _ = ObserveAsync();
    }
    private async Task ObserveAsync()
    {
        try { await _presenter!.BurstParticlesAsync(_request, _scopeCancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested) { }
        catch { }
        finally { TryComplete(); }
    }
}
