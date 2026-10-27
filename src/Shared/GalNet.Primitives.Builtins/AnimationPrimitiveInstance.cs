using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

public sealed class AnimationPrimitiveInstance : PrimitiveInstance
{
    private readonly object _gate = new();
    private readonly bool _restoring;
    private readonly IGameContext _runtime;
    private readonly IAnimationPresenter? _presenter;
    private readonly AnimationRequest _request;
    private readonly CancellationToken _scopeCancellation;
    private bool _skippable;
    private bool _skipRequested;
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
        IGameContext runtime,
        IAnimationPresenter? presenter,
        AnimationRequest request,
        string? batchId,
        CancellationToken scopeCancellation, bool restoring = false)
        : base(batchId)
    {
        _restoring = restoring;
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
        if (!_restoring) BuiltinRuntimeActions.ApplyAnimationStableState(_runtime, _request);
        else _runtime.Scene().GetOrAddTransient(_request.PlaybackHandleId,
            id => new AnimationPlaybackInstance { Id = id, LoopMode = _request.LoopMode });
        _playback = _runtime.Scene().GetOrAddTransient(_request.PlaybackHandleId,
            id => new AnimationPlaybackInstance { Id = id, LoopMode = _request.LoopMode });
        if (_presenter is null)
        {
            if (_request.LoopMode == AnimationLoopMode.Once)
                BuiltinRuntimeActions.StopAnimationState(_runtime, _request.PlaybackHandleId);
            TryComplete();
            return;
        }

        _presentationTask = ObservePresentationAsync();
        _playback.PresentationCompletion = _presentationTask;
    }

    protected override void OnSkip()
    {
        lock (_gate)
        {
            if (!_skippable || IsCompleted)
                return;
            _skippable = false;
            _skipRequested = true;
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
            var outcome = await _presenter!.AnimateAsync(_request, _scopeCancellation).ConfigureAwait(false);
            if (outcome != AnimationOutcome.Replaced && !_skipRequested && !_catchUpDetached && !_scopeCancellation.IsCancellationRequested &&
                _request.LoopMode != AnimationLoopMode.Once && (_playback?.RequestedStop is not null || outcome == AnimationOutcome.Skipped))
                BuiltinRuntimeActions.ApplyAnimationTerminalValue(_runtime, _request);
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested)
        { }
        catch (Exception error) { GameLog.Logger.Warning(error, "Primitive presentation failed"); }
        finally
        {
            _runtime.State.Update(() =>
            {
                if (!_catchUpDetached && _playback is not null && _runtime.Scene().TryGet<AnimationPlaybackInstance>(_request.PlaybackHandleId, out var current) && ReferenceEquals(current, _playback))
                    BuiltinRuntimeActions.StopAnimationState(_runtime, _request.PlaybackHandleId);
            });
            lock (_gate)
                _skippable = false;
            TryComplete();
        }
    }
}
