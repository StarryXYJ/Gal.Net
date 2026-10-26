using System.Diagnostics;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Avalonia.GameView.Presentation;

public sealed class AvaloniaAnimationPresenter : IAnimationPresenter, IDisposable
{
    private readonly GamePageViewModel _state;
    private readonly IAvaloniaUiDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ActiveAnimation> _activeAnimations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveTimelineTrack> _activeTimelineTracks = new(StringComparer.Ordinal);
    private readonly List<ActiveAnimation> _activeAdditiveAnimations = [];
    private readonly List<ActiveTimelineTrack> _activeAdditiveTimelineTracks = [];
    private readonly Dictionary<string, double> _additiveBaseValues = new(StringComparer.Ordinal);
    private readonly List<ActiveTimeline> _activeTimelines = [];
    private readonly Dictionary<string, ICompletablePlayback> _activePlaybacks = new(StringComparer.Ordinal);
    private long _sequence;

    public AvaloniaAnimationPresenter(GamePageViewModel state, IAvaloniaUiDispatcher dispatcher)
    {
        _state = state;
        _dispatcher = dispatcher;
    }

    public async Task<AnimationOutcome> AnimateAsync(
        AnimationRequest request,
        CancellationToken cancellationToken)
    {
        var key = $"{request.HandleId}:{request.Property}";
        var active = new ActiveAnimation(request, Interlocked.Increment(ref _sequence));
        lock (_gate)
        {
            if (_activePlaybacks.ContainsKey(request.PlaybackHandleId)) return AnimationOutcome.Replaced;
            if (request.BlendMode == AnimationBlendMode.Additive)
                _activeAdditiveAnimations.Add(active);
            else
            {
                if (_activeTimelineTracks.Remove(key, out var timelineTrack)) timelineTrack.Complete(AnimationOutcome.Replaced);
                if (_activeAnimations.Remove(key, out var replaced)) replaced.Complete(AnimationOutcome.Replaced);
                _activeAnimations.Add(key, active);
            }
            _activePlaybacks.Add(request.PlaybackHandleId, active);
        }

        try
        {
            var from = request.From ?? (request.BlendMode == AnimationBlendMode.Additive
                ? 0f
                : (float)await _dispatcher.InvokeAsync(() =>
                    Task.FromResult(TryGetAnimationValue(request.HandleId, request.Property, out var value)
                        ? value
                        : double.NaN)));
            if (double.IsNaN(from)) return AnimationOutcome.Replaced;
            if (request.From.HasValue || request.BlendMode == AnimationBlendMode.Additive)
                await SetAnimationValueAsync(request, active, from);

            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (active.Outcome.Task.IsCompleted)
                {
                    var outcome = await active.Outcome.Task;
                    if (outcome == AnimationOutcome.Skipped)
                        await SetAnimationValueAsync(request, active, request.To);
                    return outcome;
                }

                var cycleLength = request.LoopMode == AnimationLoopMode.PingPong ? 2d : 1d;
                var elapsedCycles = request.DurationSeconds <= 0
                    ? cycleLength
                    : stopwatch.Elapsed.TotalSeconds / request.DurationSeconds;
                var progress = request.LoopMode == AnimationLoopMode.Once
                    ? Math.Min(cycleLength, elapsedCycles)
                    : elapsedCycles % cycleLength;
                var sampleProgress = request.LoopMode == AnimationLoopMode.PingPong && progress > 1
                    ? 2 - progress
                    : progress;
                await SetAnimationValueAsync(
                    request,
                    active,
                    Lerp(from, request.To, request.Curve.Evaluate((float)sampleProgress)));
                if (request.LoopMode == AnimationLoopMode.Once && progress >= cycleLength)
                    return AnimationOutcome.Completed;

                await Task.WhenAny(
                    Task.Delay(TimeSpan.FromMilliseconds(8), cancellationToken),
                    active.Outcome.Task);
            }
        }
        finally
        {
            if (request.BlendMode == AnimationBlendMode.Additive)
                await _dispatcher.InvokeAsync(() =>
                {
                    if (request.LoopMode != AnimationLoopMode.Once)
                        RemoveAdditiveValue(active, request.HandleId, request.Property);
                    else
                        BakeAdditiveValue(active, request.HandleId, request.Property);
                    return Task.CompletedTask;
                });
            lock (_gate)
            {
                if (_activeAnimations.TryGetValue(key, out var current) && ReferenceEquals(current, active))
                    _activeAnimations.Remove(key);
                _activeAdditiveAnimations.Remove(active);
                if (_activePlaybacks.TryGetValue(request.PlaybackHandleId, out var playback) &&
                    ReferenceEquals(playback, active))
                    _activePlaybacks.Remove(request.PlaybackHandleId);
            }
        }
    }

    public async Task<TimelinePlayResult> PlayTimelineAsync(
        TimelineDefinition timeline,
        CancellationToken cancellationToken, Func<double, Task>? beforeFrame = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = new ActiveTimeline(timeline, Interlocked.Increment(ref _sequence));
        lock (_gate)
        {
            if (_activePlaybacks.ContainsKey(timeline.PlaybackHandleId))
                return new TimelinePlayResult { Outcome = AnimationOutcome.Replaced };
            _activeTimelines.Add(active);
            _activePlaybacks.Add(timeline.PlaybackHandleId, active);
        }

        try
        {
            var started = Stopwatch.StartNew();
            var tracks = RegisterTimelineTracks(active, timeline.Tracks);
            AnimationOutcome outcome;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (active.Completion.Task.IsCompleted)
                {
                    outcome = await active.Completion.Task;
                    if (outcome == AnimationOutcome.Skipped)
                        await ApplyTimelineFrameAsync(tracks, timeline.DurationFrames);
                    break;
                }

                var elapsedFrames = started.Elapsed.TotalSeconds * timeline.FrameRate;
                if (beforeFrame is not null) await beforeFrame(elapsedFrames);
                var frame = GetTimelineFrame(timeline, elapsedFrames);
                await ApplyTimelineFrameAsync(tracks, frame);
                if (timeline.LoopMode == AnimationLoopMode.Once && elapsedFrames >= timeline.DurationFrames)
                {
                    outcome = AnimationOutcome.Completed;
                    break;
                }

                await Task.WhenAny(
                    Task.Delay(TimeSpan.FromMilliseconds(8), cancellationToken),
                    active.Completion.Task);
            }

            var outcomes = tracks.Select(track => track.Completion.Task.IsCompleted
                ? track.Completion.Task.GetAwaiter().GetResult()
                : outcome).ToArray();
            return new TimelinePlayResult
            {
                Outcome = outcome,
                TrackOutcomes = timeline.Tracks.Select((track, index) => (track, outcome: outcomes[index]))
                    .ToDictionary(
                        item => $"{item.track.HandleId}:{item.track.Property}",
                        item => item.outcome,
                        StringComparer.Ordinal)
            };
        }
        finally
        {
            await CleanupTimelineTracksAsync(active);
        }
    }

    public bool CompleteAnimationImmediately(string playbackHandleId)
    {
        lock (_gate)
        {
            if (!_activePlaybacks.TryGetValue(playbackHandleId, out var playback)) return false;
            playback.Complete(AnimationOutcome.Skipped);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
            foreach (var playback in _activePlaybacks.Values.ToArray())
                playback.Complete(AnimationOutcome.Replaced);
    }

    private Task SetAnimationValueAsync(AnimationRequest request, ActiveAnimation active, double value) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (request.BlendMode == AnimationBlendMode.Additive)
                ApplyAdditiveValue(active, request.HandleId, request.Property, value);
            else
                ApplyReplaceValue(request.HandleId, request.Property, value);
            return Task.CompletedTask;
        });

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private List<ActiveTimelineTrack> RegisterTimelineTracks(
        ActiveTimeline timeline,
        IEnumerable<TimelineTrackDefinition> tracks)
    {
        var registered = new List<ActiveTimelineTrack>();
        lock (_gate)
        {
            foreach (var track in tracks)
            {
                var activeTrack = new ActiveTimelineTrack(track);
                if (track.BlendMode == AnimationBlendMode.Additive)
                    _activeAdditiveTimelineTracks.Add(activeTrack);
                else
                {
                    if (_activeAnimations.Remove(activeTrack.Key, out var animation))
                        animation.Complete(AnimationOutcome.Replaced);
                    if (_activeTimelineTracks.Remove(activeTrack.Key, out var replaced))
                        replaced.Complete(AnimationOutcome.Replaced);
                    _activeTimelineTracks.Add(activeTrack.Key, activeTrack);
                }
                timeline.Tracks.Add(activeTrack);
                registered.Add(activeTrack);
            }
        }
        return registered;
    }

    private Task ApplyTimelineFrameAsync(IReadOnlyList<ActiveTimelineTrack> tracks, double frame) =>
        _dispatcher.InvokeAsync(() =>
        {
            foreach (var activeTrack in tracks)
            {
                if (activeTrack.Completion.Task.IsCompleted) continue;
                var value = TimelineSampler.Evaluate(activeTrack.Track, frame);
                if (activeTrack.Track.BlendMode == AnimationBlendMode.Additive)
                    ApplyAdditiveValue(
                        activeTrack,
                        activeTrack.Track.HandleId,
                        activeTrack.Track.Property,
                        value);
                else
                    ApplyReplaceValue(activeTrack.Track.HandleId, activeTrack.Track.Property, value);
            }
            return Task.CompletedTask;
        });

    private static double GetTimelineFrame(TimelineDefinition timeline, double elapsedFrames)
    {
        if (timeline.LoopMode == AnimationLoopMode.Once)
            return Math.Min(timeline.DurationFrames, elapsedFrames);
        if (timeline.DurationFrames <= 0) return 0;
        if (timeline.LoopMode == AnimationLoopMode.PingPong)
        {
            var position = elapsedFrames % (timeline.DurationFrames * 2d);
            return position <= timeline.DurationFrames
                ? position
                : (timeline.DurationFrames * 2d) - position;
        }
        return elapsedFrames % timeline.DurationFrames;
    }

    private async Task CleanupTimelineTracksAsync(ActiveTimeline timeline)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var track in timeline.Tracks.Where(track => track.Track.BlendMode == AnimationBlendMode.Additive))
            {
                if (timeline.Timeline.LoopMode != AnimationLoopMode.Once)
                    RemoveAdditiveValue(track, track.Track.HandleId, track.Track.Property);
                else
                    BakeAdditiveValue(track, track.Track.HandleId, track.Track.Property);
            }
            return Task.CompletedTask;
        });
        lock (_gate)
        {
            _activeTimelines.Remove(timeline);
            if (_activePlaybacks.TryGetValue(timeline.Timeline.PlaybackHandleId, out var playback) &&
                ReferenceEquals(playback, timeline))
                _activePlaybacks.Remove(timeline.Timeline.PlaybackHandleId);
            foreach (var track in timeline.Tracks)
            {
                if (_activeTimelineTracks.TryGetValue(track.Key, out var current) && ReferenceEquals(current, track))
                    _activeTimelineTracks.Remove(track.Key);
                _activeAdditiveTimelineTracks.Remove(track);
            }
        }
    }

    private void ApplyReplaceValue(string handleId, string property, double value)
    {
        _additiveBaseValues[PropertyKey(handleId, property)] = value;
        ApplyActiveAdditiveValues(handleId, property);
    }

    private void ApplyAdditiveValue(IAdditiveAnimation animation, string handleId, string property, double value)
    {
        var key = PropertyKey(handleId, property);
        if (!TryGetAdditiveBaseValue(key, handleId, property, out var baseValue)) return;
        animation.CurrentValue = value;
        SetAnimationValue(
            handleId,
            property,
            ClampPresentationValue(handleId, property, baseValue + GetAdditiveContribution(handleId, property)));
    }

    private void ApplyActiveAdditiveValues(string handleId, string property)
    {
        var key = PropertyKey(handleId, property);
        if (TryGetAdditiveBaseValue(key, handleId, property, out var baseValue))
            SetAnimationValue(
                handleId,
                property,
                ClampPresentationValue(handleId, property, baseValue + GetAdditiveContribution(handleId, property)));
    }

    private void BakeAdditiveValue(IAdditiveAnimation animation, string handleId, string property)
    {
        var key = PropertyKey(handleId, property);
        if (_additiveBaseValues.TryGetValue(key, out var baseValue))
            _additiveBaseValues[key] = baseValue + animation.CurrentValue;
        animation.CurrentValue = 0;
    }

    private void RemoveAdditiveValue(IAdditiveAnimation animation, string handleId, string property)
    {
        animation.CurrentValue = 0;
        ApplyActiveAdditiveValues(handleId, property);
    }

    private bool TryGetAdditiveBaseValue(string key, string handleId, string property, out double value)
    {
        if (_additiveBaseValues.TryGetValue(key, out value)) return true;
        if (!TryGetAnimationValue(handleId, property, out value)) return false;
        _additiveBaseValues.Add(key, value);
        return true;
    }

    private double GetAdditiveContribution(string handleId, string property)
    {
        lock (_gate)
            return _activeAdditiveAnimations
                .Where(animation => animation.Request.HandleId == handleId && animation.Request.Property == property)
                .Sum(animation => animation.CurrentValue) +
                _activeAdditiveTimelineTracks
                    .Where(track => track.Track.HandleId == handleId && track.Track.Property == property)
                    .Sum(track => track.CurrentValue);
    }

    private static string PropertyKey(string handleId, string property) => $"{handleId}:{property}";

    private bool TryGetAnimationValue(string handleId, string property, out double value) =>
        _state.TryGetLayerAnimationValue(handleId, property, out value) ||
        _state.TryGetEffectAnimationValue(handleId, property, out value) ||
        _state.TryGetParticleAnimationValue(handleId, property, out value);

    private bool SetAnimationValue(string handleId, string property, double value) =>
        _state.SetLayerAnimationValue(handleId, property, value) ||
        _state.SetEffectAnimationValue(handleId, property, value) ||
        _state.SetParticleAnimationValue(handleId, property, value);

    private bool IsLayerAnimationValue(string handleId, string property) =>
        _state.TryGetLayerAnimationValue(handleId, property, out _);

    private double ClampPresentationValue(string handleId, string property, double value) =>
        IsLayerAnimationValue(handleId, property) ? ClampLayerValue(property, value) : value;

    private static double ClampLayerValue(string property, double value) => property switch
    {
        "opacity" => Math.Clamp(value, 0, 1),
        "transform.scaleX" or "transform.scaleY" => Math.Max(.001, value),
        _ => value
    };

    private interface ICompletablePlayback
    {
        void Complete(AnimationOutcome outcome);
    }

    private interface IAdditiveAnimation
    {
        double CurrentValue { get; set; }
    }

    private sealed class ActiveAnimation(AnimationRequest request, long sequence)
        : ICompletablePlayback, IAdditiveAnimation
    {
        public AnimationRequest Request { get; } = request;
        public long Sequence { get; } = sequence;
        public double CurrentValue { get; set; }
        public TaskCompletionSource<AnimationOutcome> Outcome { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(AnimationOutcome outcome) => Outcome.TrySetResult(outcome);
    }

    private sealed class ActiveTimeline(TimelineDefinition timeline, long sequence) : ICompletablePlayback
    {
        public TimelineDefinition Timeline { get; } = timeline;
        public long Sequence { get; } = sequence;
        public List<ActiveTimelineTrack> Tracks { get; } = [];
        public TaskCompletionSource<AnimationOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(AnimationOutcome outcome) => Completion.TrySetResult(outcome);
    }

    private sealed class ActiveTimelineTrack(TimelineTrackDefinition track) : IAdditiveAnimation
    {
        public TimelineTrackDefinition Track { get; } = track;
        public string Key { get; } = $"{track.HandleId}:{track.Property}";
        public double CurrentValue { get; set; }
        public TaskCompletionSource<AnimationOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(AnimationOutcome outcome) => Completion.TrySetResult(outcome);
    }
}
