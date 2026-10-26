using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

public sealed class TimelinePrimitiveInstance : PrimitiveInstance, IDisposable
{
    private readonly object _gate = new();
    private readonly bool _restoring;
    private readonly IGameContext _runtime;
    private readonly IAnimationPresenter? _animationPresenter;
    private readonly ILayerPresenter? _layerPresenter;
    private readonly IEffectPresenter? _effectPresenter;
    private readonly IParticlePresenter? _particlePresenter;
    private readonly TimelineDefinition _timeline;
    private readonly CancellationToken _scopeCancellation;
    private readonly IndexedTimelineEvent[] _events;
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

    public TimelinePrimitiveInstance(IAnimationPresenter? animationPresenter, ILayerPresenter? layerPresenter,
        IEffectPresenter? effectPresenter, IParticlePresenter? particlePresenter, PrimitiveCreateContext context, TimelineOptions options)
        : this(context.Runtime, animationPresenter, layerPresenter, effectPresenter, particlePresenter,
            BuiltinRuntimeActions.CreateTimeline(options), context.BatchId, context.ScopeCancellation)
    { }

    public TimelinePrimitiveInstance(
        IGameContext runtime,
        IAnimationPresenter? animationPresenter,
        ILayerPresenter? layerPresenter,
        IEffectPresenter? effectPresenter,
        IParticlePresenter? particlePresenter,
        TimelineDefinition timeline,
        string? batchId,
        CancellationToken scopeCancellation, bool restoring = false)
        : base(batchId)
    {
        _restoring = restoring;
        _runtime = runtime;
        _animationPresenter = animationPresenter;
        _layerPresenter = layerPresenter;
        _effectPresenter = effectPresenter;
        _particlePresenter = particlePresenter;
        _timeline = timeline;
        _scopeCancellation = scopeCancellation;
        _skippable = timeline.Skippable;
        _events = timeline.Events
            .Select((item, index) => new IndexedTimelineEvent(index, item))
            .OrderBy(item => item.Event.Frame)
            .ThenBy(item => item.Index)
            .ToArray();
        _notifiedEvents = new bool[_events.Length];
        if (restoring)
            foreach (var item in _events.Where(item => item.Event.Frame <= 0)) _notifiedEvents[item.Index] = true;
    }

    public override bool IsBlocking => _timeline.Blocking;
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
        if (!_restoring) BuiltinRuntimeActions.ApplyTimelineStableState(_runtime, _timeline);
        else _runtime.SceneInstances.GetOrAddTransient(_timeline.PlaybackHandleId,
            id => new AnimationPlaybackInstance { Id = id, LoopMode = _timeline.LoopMode });
        _runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(_timeline.PlaybackHandleId, out _playback!);
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

        _animationPresenter?.CompleteAnimationImmediately(_timeline.PlaybackHandleId);
        if (_timeline.LoopMode != AnimationLoopMode.Once)
            BuiltinRuntimeActions.ApplyTimelineTerminalState(_runtime, _timeline);
        _skipTask = CompleteSkipAsync();
    }

    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
    {
        if (mode == PrimitiveInvocationMode.CatchUp && !_timeline.Blocking && _timeline.LoopMode != AnimationLoopMode.Once)
        {
            _catchUpDetached = true;
            _presentationCancellation.Cancel();
            _animationPresenter?.CompleteAnimationImmediately(_timeline.PlaybackHandleId);
            return;
        }
        if (mode != PrimitiveInvocationMode.Normal && (_timeline.Blocking || _timeline.LoopMode == AnimationLoopMode.Once)) Skip();
    }

    protected override void OnCatchUpDispatch()
    {
        if (!_timeline.Skippable && _timeline.Blocking) { OnDispatch(); return; }
        BuiltinRuntimeActions.ApplyTimelineStableState(_runtime, _timeline);
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
            await _animationPresenter!.PlayTimelineAsync(
                _timeline, linked.Token, elapsedFrames => NotifyPlaybackEventsAsync(elapsedFrames, linked.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_scopeCancellation.IsCancellationRequested || _presentationCancellation.IsCancellationRequested)
        { }
        catch (Exception error)
        { GameLog.Logger.Warning(error, "Animation timeline {PlaybackId} presentation failed", _timeline.PlaybackHandleId); }
        finally
        {
            _runtime.State.Update(() =>
            {
                if (!_catchUpDetached && _playback is not null && _runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(_timeline.PlaybackHandleId, out var current) && ReferenceEquals(current, _playback))
                    BuiltinRuntimeActions.StopAnimationState(_runtime, _timeline.PlaybackHandleId);
            });
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
        var cycleFrames = _timeline.LoopMode == AnimationLoopMode.PingPong
            ? _timeline.DurationFrames * 2d : _timeline.DurationFrames;
        var cycle = _timeline.LoopMode == AnimationLoopMode.Once || cycleFrames <= 0
            ? 0 : (long)(elapsedFrames / cycleFrames);
        for (; _eventCycle < cycle; _eventCycle++)
        {
            await NotifyEventsThroughFrameAsync(_timeline.DurationFrames, cancellationToken).ConfigureAwait(false);
            lock (_gate) Array.Clear(_notifiedEvents);
        }
        var frame = _timeline.LoopMode == AnimationLoopMode.Once || cycleFrames <= 0
            ? elapsedFrames : elapsedFrames % cycleFrames;
        await NotifyEventsThroughFrameAsync((int)Math.Min(_timeline.DurationFrames, frame), cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyRemainingEventsAsync(CancellationToken cancellationToken)
    {
        foreach (var item in _events)
        {
            if (item.Event.Type == BuiltinEntryIds.BurstParticles)
                continue;
            await NotifyEventAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task NotifyEventAsync(IndexedTimelineEvent item, CancellationToken cancellationToken)
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
                await BuiltinRuntimeActions.NotifyTimelineEventAsync(
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
                GameLog.Logger.Warning(error, "Animation timeline event {EntryType} presentation failed", item.Event.Type);
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

    private sealed record IndexedTimelineEvent(int Index, TimelineEventDefinition Event);
}
