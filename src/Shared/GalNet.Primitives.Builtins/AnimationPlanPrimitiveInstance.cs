using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Core.Entry;

namespace GalNet.Primitives.Builtins;

public sealed class AnimationPlanPrimitiveInstance : PrimitiveInstance
{
    private readonly object _gate = new();
    private readonly IGameRuntime _runtime;
    private readonly IAnimationPresenter? _animationPresenter;
    private readonly ILayerPresenter? _layerPresenter;
    private readonly IEffectPresenter? _effectPresenter;
    private readonly IParticlePresenter? _particlePresenter;
    private readonly AnimationPlanDefinition _plan;
    private readonly CancellationToken _scopeCancellation;
    private readonly IndexedPlanEvent[] _events;
    private readonly bool[] _notifiedEvents;
    private readonly CancellationTokenSource _presentationCancellation = new();
    private bool _skippable;

    public AnimationPlanPrimitiveInstance(
        IGameRuntime runtime,
        IAnimationPresenter? animationPresenter,
        ILayerPresenter? layerPresenter,
        IEffectPresenter? effectPresenter,
        IParticlePresenter? particlePresenter,
        AnimationPlanDefinition plan,
        string? batchId,
        CancellationToken scopeCancellation)
        : base(batchId)
    {
        _runtime = runtime;
        _animationPresenter = animationPresenter;
        _layerPresenter = layerPresenter;
        _effectPresenter = effectPresenter;
        _particlePresenter = particlePresenter;
        _plan = plan;
        _scopeCancellation = scopeCancellation;
        _skippable = plan.Skippable;
        _events = plan.Events
            .Select((item, index) => new IndexedPlanEvent(index, item))
            .OrderBy(item => item.Event.Frame)
            .ThenBy(item => item.Index)
            .ToArray();
        _notifiedEvents = new bool[_events.Length];
    }

    public override bool IsBlocking => _plan.Blocking;
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
        BuiltinRuntimeActions.ApplyPlanStableState(_runtime, _plan);
        if (_animationPresenter is null)
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

        _presentationCancellation.Cancel();
        _animationPresenter?.CompleteAnimationImmediately(_plan.PlaybackHandleId);
        BuiltinRuntimeActions.ApplyPlanTerminalState(_runtime, _plan);
        _ = NotifyRemainingEventsAsync(CancellationToken.None);
        TryComplete();
    }

    private async Task ObservePresentationAsync()
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _scopeCancellation,
                _presentationCancellation.Token);
            await NotifyEventsThroughFrameAsync(0, linked.Token).ConfigureAwait(false);
            var animation = _animationPresenter!.PlayAnimationPlanAsync(_plan, linked.Token);
            var events = NotifyFutureEventsAsync(linked.Token);
            await animation.ConfigureAwait(false);
            if (_plan.LoopMode != AnimationLoopMode.Once)
                _presentationCancellation.Cancel();
            await events.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested || _presentationCancellation.IsCancellationRequested)
        { }
        catch
        { }
        finally
        {
            BuiltinRuntimeActions.StopAnimationState(_runtime, _plan.PlaybackHandleId);
            lock (_gate)
                _skippable = false;
            TryComplete();
            _presentationCancellation.Dispose();
        }
    }

    private async Task NotifyEventsThroughFrameAsync(int frame, CancellationToken cancellationToken)
    {
        foreach (var item in _events.Where(item => item.Event.Frame <= frame))
            await NotifyEventAsync(item, cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyFutureEventsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var started = DateTimeOffset.UtcNow;
            foreach (var item in _events.Where(item => item.Event.Frame > 0))
            {
                var delay = TimeSpan.FromSeconds(item.Event.Frame / (double)Math.Max(1, _plan.FrameRate)) -
                            (DateTimeOffset.UtcNow - started);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                await NotifyEventAsync(item, cancellationToken).ConfigureAwait(false);
            }

            if (_plan.LoopMode == AnimationLoopMode.Once)
                return;
            if (_plan.DurationFrames <= 0)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return;
            }

            var cycleFrames = _plan.LoopMode == AnimationLoopMode.PingPong
                ? _plan.DurationFrames * 2d
                : _plan.DurationFrames;
            var remainder = TimeSpan.FromSeconds(cycleFrames / Math.Max(1, _plan.FrameRate)) -
                            (DateTimeOffset.UtcNow - started);
            if (remainder > TimeSpan.Zero)
                await Task.Delay(remainder, cancellationToken).ConfigureAwait(false);

            lock (_gate)
                Array.Clear(_notifiedEvents);
            await NotifyEventsThroughFrameAsync(0, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task NotifyRemainingEventsAsync(CancellationToken cancellationToken)
    {
        foreach (var item in _events)
        {
            if (item.Event.Type == BurstParticlesEntry.TypeId)
                continue;
            await NotifyEventAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task NotifyEventAsync(IndexedPlanEvent item, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_notifiedEvents[item.Index])
                return;
            _notifiedEvents[item.Index] = true;
        }

        try
        {
            await BuiltinRuntimeActions.NotifyPlanEventAsync(
                item.Event,
                _layerPresenter,
                _effectPresenter,
                _particlePresenter,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        { }
    }

    private sealed record IndexedPlanEvent(int Index, AnimationPlanEventDefinition Event);
}
