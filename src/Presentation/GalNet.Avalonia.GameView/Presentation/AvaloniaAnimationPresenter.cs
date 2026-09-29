using System.Diagnostics;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Avalonia.GameView.Presentation;

public sealed class AvaloniaAnimationPresenter : IAnimationPresenter, IDisposable
{
    private readonly GamePageViewModel _state;
    private readonly IAvaloniaUiDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ActiveAnimation> _activeAnimations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivePlanTrack> _activePlanTracks = new(StringComparer.Ordinal);
    private readonly List<ActiveAnimation> _activeAdditiveAnimations = [];
    private readonly List<ActivePlanTrack> _activeAdditivePlanTracks = [];
    private readonly Dictionary<string, double> _additiveBaseValues = new(StringComparer.Ordinal);
    private readonly List<ActivePlan> _activePlans = [];
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
                if (_activePlanTracks.Remove(key, out var planTrack)) planTrack.Complete(AnimationOutcome.Replaced);
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

    public async Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(
        AnimationPlanDefinition plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = new ActivePlan(plan, Interlocked.Increment(ref _sequence));
        lock (_gate)
        {
            if (_activePlaybacks.ContainsKey(plan.PlaybackHandleId))
                return new AnimationPlanPlayResult { Outcome = AnimationOutcome.Replaced };
            _activePlans.Add(active);
            _activePlaybacks.Add(plan.PlaybackHandleId, active);
        }

        try
        {
            var started = Stopwatch.StartNew();
            var tracks = RegisterPlanTracks(active, plan.Tracks);
            AnimationOutcome outcome;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (active.Completion.Task.IsCompleted)
                {
                    outcome = await active.Completion.Task;
                    if (outcome == AnimationOutcome.Skipped)
                        await ApplyPlanFrameAsync(tracks, plan.DurationFrames);
                    break;
                }

                var elapsedFrames = started.Elapsed.TotalSeconds * plan.FrameRate;
                var frame = GetPlanFrame(plan, elapsedFrames);
                await ApplyPlanFrameAsync(tracks, frame);
                if (plan.LoopMode == AnimationLoopMode.Once && elapsedFrames >= plan.DurationFrames)
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
            return new AnimationPlanPlayResult
            {
                Outcome = outcome,
                TrackOutcomes = plan.Tracks.Select((track, index) => (track, outcome: outcomes[index]))
                    .ToDictionary(
                        item => $"{item.track.HandleId}:{item.track.Property}",
                        item => item.outcome,
                        StringComparer.Ordinal)
            };
        }
        finally
        {
            await CleanupPlanTracksAsync(active);
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

    private List<ActivePlanTrack> RegisterPlanTracks(
        ActivePlan plan,
        IEnumerable<AnimationTrackDefinition> tracks)
    {
        var registered = new List<ActivePlanTrack>();
        lock (_gate)
        {
            foreach (var track in tracks)
            {
                var activeTrack = new ActivePlanTrack(track);
                if (track.BlendMode == AnimationBlendMode.Additive)
                    _activeAdditivePlanTracks.Add(activeTrack);
                else
                {
                    if (_activeAnimations.Remove(activeTrack.Key, out var animation))
                        animation.Complete(AnimationOutcome.Replaced);
                    if (_activePlanTracks.Remove(activeTrack.Key, out var replaced))
                        replaced.Complete(AnimationOutcome.Replaced);
                    _activePlanTracks.Add(activeTrack.Key, activeTrack);
                }
                plan.Tracks.Add(activeTrack);
                registered.Add(activeTrack);
            }
        }
        return registered;
    }

    private Task ApplyPlanFrameAsync(IReadOnlyList<ActivePlanTrack> tracks, double frame) =>
        _dispatcher.InvokeAsync(() =>
        {
            foreach (var activeTrack in tracks)
            {
                if (activeTrack.Completion.Task.IsCompleted) continue;
                var value = AnimationTrackSampler.Evaluate(activeTrack.Track, frame);
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

    private static double GetPlanFrame(AnimationPlanDefinition plan, double elapsedFrames)
    {
        if (plan.LoopMode == AnimationLoopMode.Once)
            return Math.Min(plan.DurationFrames, elapsedFrames);
        if (plan.DurationFrames <= 0) return 0;
        if (plan.LoopMode == AnimationLoopMode.PingPong)
        {
            var position = elapsedFrames % (plan.DurationFrames * 2d);
            return position <= plan.DurationFrames
                ? position
                : (plan.DurationFrames * 2d) - position;
        }
        return elapsedFrames % plan.DurationFrames;
    }

    private async Task CleanupPlanTracksAsync(ActivePlan plan)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var track in plan.Tracks.Where(track => track.Track.BlendMode == AnimationBlendMode.Additive))
            {
                if (plan.Plan.LoopMode != AnimationLoopMode.Once)
                    RemoveAdditiveValue(track, track.Track.HandleId, track.Track.Property);
                else
                    BakeAdditiveValue(track, track.Track.HandleId, track.Track.Property);
            }
            return Task.CompletedTask;
        });
        lock (_gate)
        {
            _activePlans.Remove(plan);
            if (_activePlaybacks.TryGetValue(plan.Plan.PlaybackHandleId, out var playback) &&
                ReferenceEquals(playback, plan))
                _activePlaybacks.Remove(plan.Plan.PlaybackHandleId);
            foreach (var track in plan.Tracks)
            {
                if (_activePlanTracks.TryGetValue(track.Key, out var current) && ReferenceEquals(current, track))
                    _activePlanTracks.Remove(track.Key);
                _activeAdditivePlanTracks.Remove(track);
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
                _activeAdditivePlanTracks
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

    private sealed class ActivePlan(AnimationPlanDefinition plan, long sequence) : ICompletablePlayback
    {
        public AnimationPlanDefinition Plan { get; } = plan;
        public long Sequence { get; } = sequence;
        public List<ActivePlanTrack> Tracks { get; } = [];
        public TaskCompletionSource<AnimationOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(AnimationOutcome outcome) => Completion.TrySetResult(outcome);
    }

    private sealed class ActivePlanTrack(AnimationTrackDefinition track) : IAdditiveAnimation
    {
        public AnimationTrackDefinition Track { get; } = track;
        public string Key { get; } = $"{track.HandleId}:{track.Property}";
        public double CurrentValue { get; set; }
        public TaskCompletionSource<AnimationOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(AnimationOutcome outcome) => Completion.TrySetResult(outcome);
    }
}
