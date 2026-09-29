using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class AnimationPrimitiveInstance : PrimitiveInstance
{
    private readonly object _gate = new();
    private readonly IGameRuntime _runtime;
    private readonly IAnimationPresenter? _presenter;
    private readonly AnimationRequest _request;
    private readonly CancellationToken _scopeCancellation;
    private bool _skippable;

    public AnimationPrimitiveInstance(
        IGameRuntime runtime,
        IAnimationPresenter? presenter,
        AnimationRequest request,
        string? batchId,
        CancellationToken scopeCancellation)
        : base(batchId)
    {
        _runtime = runtime;
        _presenter = presenter;
        _request = request;
        _scopeCancellation = scopeCancellation;
        _skippable = request.Skippable;
    }

    public override bool IsBlocking => _request.Blocking;
    public override bool IsSkippable
    {
        get
        {
            lock (_gate)
                return _skippable;
        }
    }

    protected override void OnDispatch()
    {
        BuiltinRuntimeActions.ApplyAnimationStableState(_runtime, _request);
        if (_presenter is null)
        {
            TryComplete();
            return;
        }

        _ = ObservePresentationAsync();
    }

    protected override void OnSkip()
    {
        lock (_gate)
        {
            if (!_skippable || IsCompleted)
                return;
            _skippable = false;
        }

        _presenter?.CompleteAnimationImmediately(_request.PlaybackHandleId);
        BuiltinRuntimeActions.ApplyAnimationTerminalValue(_runtime, _request);
        TryComplete();
    }

    private async Task ObservePresentationAsync()
    {
        try
        {
            await _presenter!.AnimateAsync(_request, _scopeCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested)
        { }
        catch
        { }
        finally
        {
            BuiltinRuntimeActions.StopAnimationState(_runtime, _request.PlaybackHandleId);
            lock (_gate)
                _skippable = false;
            TryComplete();
        }
    }
}
