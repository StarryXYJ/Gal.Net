using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

public sealed class AnimationPlanPrimitiveInstance : PrimitiveInstance, IDisposable
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
    private readonly SemaphoreSlim _notificationGate = new(1, 1);
    private int _resourcesDisposed;
    private bool _skipRequested;
    private bool _skippable;
    private AnimationPlaybackInstance? _playback;
    private long _eventCycle;
    private bool _catchUpDetached;
    private Task _presentationTask = Task.CompletedTask;
    private Task _skipTask = Task.CompletedTask;
    public override Task PresentationCompletion => Task.WhenAll(_presentationTask, _skipTask);

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
        _runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(_plan.PlaybackHandleId, out _playback!);
        if (_animationPresenter is null)
        {
            Dispose();
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
            _skipRequested = true;
            _presentationCancellation.Cancel();
        }

        _animationPresenter?.CompleteAnimationImmediately(_plan.PlaybackHandleId);
        if (_plan.LoopMode != AnimationLoopMode.Once)
            BuiltinRuntimeActions.ApplyPlanTerminalState(_runtime, _plan);
        _skipTask = CompleteSkipAsync();
    }

    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
    {
        if (mode == PrimitiveInvocationMode.CatchUp && !_plan.Blocking && _plan.LoopMode != AnimationLoopMode.Once)
        {
            _catchUpDetached = true;
            _presentationCancellation.Cancel();
            _animationPresenter?.CompleteAnimationImmediately(_plan.PlaybackHandleId);
            return;
        }
        if (mode != PrimitiveInvocationMode.Normal && (_plan.Blocking || _plan.LoopMode == AnimationLoopMode.Once)) Skip();
    }

    protected override void OnCatchUpDispatch()
    {
        if (!_plan.Skippable && _plan.Blocking) { OnDispatch(); return; }
        BuiltinRuntimeActions.ApplyPlanStableState(_runtime, _plan);
        Dispose();
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
            await _animationPresenter!.PlayAnimationPlanAsync(
                _plan, linked.Token, elapsedFrames => NotifyPlaybackEventsAsync(elapsedFrames, linked.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested || _presentationCancellation.IsCancellationRequested)
        { }
        catch (Exception error)
        { GameLog.Logger.Warning(error, "Animation plan {PlaybackId} presentation failed", _plan.PlaybackHandleId); }
        finally
        {
            if (!_catchUpDetached && _playback is not null && _runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(_plan.PlaybackHandleId, out var current) && ReferenceEquals(current, _playback))
                BuiltinRuntimeActions.StopAnimationState(_runtime, _plan.PlaybackHandleId);
            bool skipRequested;
            lock (_gate)
            {
                _skippable = false;
                skipRequested = _skipRequested;
            }
            if (!skipRequested)
            {
                Dispose();
                TryComplete();
            }
        }
    }

    private async Task NotifyEventsThroughFrameAsync(int frame, CancellationToken cancellationToken)
    {
        foreach (var item in _events.Where(item => item.Event.Frame <= frame))
            await NotifyEventAsync(item, cancellationToken).ConfigureAwait(false);
    }

    private async Task CompleteSkipAsync()
    {
        try { await NotifyRemainingEventsAsync(_scopeCancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested) { }
        finally
        {
            Dispose();
            TryComplete();
        }
    }

    // The presenter supplies its own elapsed frame count before sampling tracks.
    // Events and values therefore share one monotonic playback clock, including stalls.
    private async Task NotifyPlaybackEventsAsync(double elapsedFrames, CancellationToken cancellationToken)
    {
        var cycleFrames = _plan.LoopMode == AnimationLoopMode.PingPong
            ? _plan.DurationFrames * 2d : _plan.DurationFrames;
        var cycle = _plan.LoopMode == AnimationLoopMode.Once || cycleFrames <= 0
            ? 0 : (long)(elapsedFrames / cycleFrames);
        for (; _eventCycle < cycle; _eventCycle++)
        {
            await NotifyEventsThroughFrameAsync(_plan.DurationFrames, cancellationToken).ConfigureAwait(false);
            lock (_gate) Array.Clear(_notifiedEvents);
        }
        var frame = _plan.LoopMode == AnimationLoopMode.Once || cycleFrames <= 0
            ? elapsedFrames : elapsedFrames % cycleFrames;
        await NotifyEventsThroughFrameAsync((int)Math.Min(_plan.DurationFrames, frame), cancellationToken).ConfigureAwait(false);
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
        await _notificationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_notifiedEvents[item.Index]) return;
                _notifiedEvents[item.Index] = true;
            }
            try
            {
                await BuiltinRuntimeActions.NotifyPlanEventAsync(
                    item.Event, _layerPresenter, _effectPresenter, _particlePresenter,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (_gate) _notifiedEvents[item.Index] = false;
                throw;
            }
            catch (Exception error)
            {
                GameLog.Logger.Warning(error, "Animation plan event {EntryType} presentation failed", item.Event.Type);
            }
        }
        finally { _notificationGate.Release(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _skippable = false;
            _skipRequested = true;
        }

        lock (_gate)
        {
            if (_resourcesDisposed != 0) return;
            _resourcesDisposed = 1;
            _presentationCancellation.Cancel();
            _presentationCancellation.Dispose();
            _notificationGate.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private sealed record IndexedPlanEvent(int Index, AnimationPlanEventDefinition Event);
}
