using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

public sealed class AnimationPrimitiveInstance : PrimitiveInstance
{
    private readonly object _gate = new();
    private readonly IGameRuntime _runtime;
    private readonly IAnimationPresenter? _presenter;
    private readonly AnimationRequest _request;
    private readonly CancellationToken _scopeCancellation;
    private bool _skippable;
    private AnimationPlaybackInstance? _playback;
    private bool _catchUpDetached;
    private Task _presentationTask = Task.CompletedTask;
    public override Task PresentationCompletion => _presentationTask;

    public AnimationPrimitiveInstance(IAnimationPresenter? presenter, PrimitiveCreateContext context, AnimationOptions options)
        : this(context.Runtime, presenter,
            BuiltinRuntimeActions.CreateAnimationRequest(options, context.Arguments.TryGetProperty("from", out _) ? options.From : null),
            context.BatchId, context.ScopeCancellation)
    { }

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
        _runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(_request.PlaybackHandleId, out _playback!);
        if (_presenter is null)
        {
            TryComplete();
            return;
        }

        _presentationTask = ObservePresentationAsync();
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
        if (_request.LoopMode != AnimationLoopMode.Once)
            BuiltinRuntimeActions.ApplyAnimationTerminalValue(_runtime, _request);
        BuiltinRuntimeActions.StopAnimationState(_runtime, _request.PlaybackHandleId);
        TryComplete();
    }

    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
    {
        if (mode == PrimitiveInvocationMode.CatchUp && !_request.Blocking && _request.LoopMode != AnimationLoopMode.Once)
        {
            _catchUpDetached = true;
            _presenter?.CompleteAnimationImmediately(_request.PlaybackHandleId);
            TryComplete();
            return;
        }
        if (mode != PrimitiveInvocationMode.Normal && (_request.Blocking || _request.LoopMode == AnimationLoopMode.Once)) Skip();
    }

    protected override void OnCatchUpDispatch()
    {
        if (!_request.Skippable && _request.Blocking) { OnDispatch(); return; }
        BuiltinRuntimeActions.ApplyAnimationStableState(_runtime, _request);
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
        catch (Exception error) { GameLog.Logger.Warning(error, "Primitive presentation failed"); }
        finally
        {
            if (!_catchUpDetached && _playback is not null && _runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(_request.PlaybackHandleId, out var current) && ReferenceEquals(current, _playback))
                BuiltinRuntimeActions.StopAnimationState(_runtime, _request.PlaybackHandleId);
            lock (_gate)
                _skippable = false;
            TryComplete();
        }
    }
}
