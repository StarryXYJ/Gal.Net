using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using GalNet.Avalonia.GameView.Page;
using GalNet.Core.View;
using GalNet.Core.Scene;
using GalNet.Game.Controls.Scene;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Host-provided creation of layer visuals; the shared page never resolves files itself.</summary>
public interface IGamePageLayerFactory
{
    /// <summary>Resolves a content asset ID to the Avalonia image displayed for a layer.</summary>
    /// <param name="assetId">Host-defined asset identifier from a Layer request.</param>
    /// <returns>The image to display, or <see langword="null"/> when the host cannot resolve the asset.</returns>
    IImage ResolveLayerImage(string assetId);
}

/// <summary>Maps runtime layer, dialogue and interaction ports onto a shared <see cref="GamePage"/>.</summary>
public sealed class AvaloniaGamePageView : ILayerView, IAnimationView, IControlView, ITypewriterView, IInteractionView, IDisposable
{
    private readonly GamePageViewModel _state;
    private readonly GamePage _page;
    private readonly IGamePageLayerFactory _layers;
    private bool _isTyping;
    private readonly Lock _animationGate = new();
    private readonly Dictionary<string, ActiveAnimation> _activeAnimations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivePlanTrack> _activePlanTracks = new(StringComparer.Ordinal);
    private readonly List<ActiveAnimation> _activeAdditiveAnimations = [];
    private readonly List<ActivePlanTrack> _activeAdditivePlanTracks = [];
    private readonly Dictionary<string, double> _additiveBaseValues = new(StringComparer.Ordinal);
    private readonly List<ActivePlan> _activePlans = [];
    private readonly Dictionary<string, ICompletablePlayback> _activePlaybacks = new(StringComparer.Ordinal);
    private long _animationSequence;

    public AvaloniaGamePageView(GamePageViewModel state, GamePage page, IGamePageLayerFactory layers)
    {
        _state = state;
        _page = page;
        _layers = layers;
        _state.AdvanceRequested += Advance;
    }

    public void ShowLayer(LayerRenderRequest request) => OnUi(() =>
        _state.SetLayer(request.HandleId, new SceneLayerItem
        {
            HandleId = request.HandleId,
            Image = request.Color is null ? _layers.ResolveLayerImage(request.AssetId) : null,
            Color = request.Color,
            Flipbook = request.Flipbook?.Clone(),
            X = request.Transform.X,
            Y = request.Transform.Y,
            RotationDegrees = request.Transform.RotationDegrees,
            ScaleX = request.Transform.ScaleX,
            ScaleY = request.Transform.ScaleY,
            Z = request.Z,
            DisplayMode = request.DisplayMode,
            Opacity = request.Opacity
        }));

    public void ReplaceLayer(string handleId, string assetId) => OnUi(() => _state.ReplaceLayer(handleId, _layers.ResolveLayerImage(assetId)));
    public void HideLayer(string handleId) => OnUi(() => _state.HideLayer(handleId));
    public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec) => OnUi(() => _state.MoveLayer(handleId, transform, z));
    public async Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken ct)
    {
        var key = $"{request.HandleId}:{request.Property}";
        var active = new ActiveAnimation(request, Interlocked.Increment(ref _animationSequence));
        lock (_animationGate)
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
                : (float)await OnUiAsync(() =>
                    Task.FromResult(TryGetAnimationValue(request.HandleId, request.Property, out var value) ? value : double.NaN)));
            if (double.IsNaN(from)) return AnimationOutcome.Replaced;
            if (request.From.HasValue || request.BlendMode == AnimationBlendMode.Additive)
                await SetAnimationValueAsync(request, active, from);

            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (active.Outcome.Task.IsCompleted)
                {
                    var outcome = await active.Outcome.Task;
                    if (outcome == AnimationOutcome.Skipped)
                        await SetAnimationValueAsync(request, active, request.To);
                    return outcome;
                }
                var cycleLength = request.LoopMode == AnimationLoopMode.PingPong ? 2d : 1d;
                var progress = request.DurationSeconds <= 0 ? cycleLength : Math.Min(cycleLength, stopwatch.Elapsed.TotalSeconds / request.DurationSeconds);
                var sampleProgress = request.LoopMode == AnimationLoopMode.PingPong && progress > 1 ? 2 - progress : progress;
                await SetAnimationValueAsync(request, active, Lerp(from, request.To, request.Curve.Evaluate((float)sampleProgress)));
                if (progress >= cycleLength) return AnimationOutcome.Completed;
                await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(16), ct), active.Outcome.Task);
            }
        }
        finally
        {
            if (request.BlendMode == AnimationBlendMode.Additive)
                await OnUiAsync(() =>
                {
                    if (request.LoopMode != AnimationLoopMode.Once)
                        RemoveAdditiveValue(active, request.HandleId, request.Property);
                    else
                        BakeAdditiveValue(active, request.HandleId, request.Property);
                    return Task.CompletedTask;
                });
            lock (_animationGate)
            {
                if (_activeAnimations.TryGetValue(key, out var current) && ReferenceEquals(current, active)) _activeAnimations.Remove(key);
                _activeAdditiveAnimations.Remove(active);
                if (_activePlaybacks.TryGetValue(request.PlaybackHandleId, out var playback) && ReferenceEquals(playback, active)) _activePlaybacks.Remove(request.PlaybackHandleId);
            }
        }
    }

    public async Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var active = new ActivePlan(plan, Interlocked.Increment(ref _animationSequence));
        lock (_animationGate)
        {
            if (_activePlaybacks.ContainsKey(plan.PlaybackHandleId))
                return new AnimationPlanPlayResult { Outcome = AnimationOutcome.Replaced };
            _activePlans.Add(active);
            _activePlaybacks.Add(plan.PlaybackHandleId, active);
        }

        try
        {
            var started = Stopwatch.StartNew();
            var tracks = plan.Tracks.Select(track => RunPlanTrackAsync(active, track, started, ct)).ToArray();
            var duration = Task.Delay(TimeSpan.FromSeconds(plan.DurationFrames / (double)plan.FrameRate), ct);
            await Task.WhenAny(duration, active.Completion.Task);
            var outcomes = await Task.WhenAll(tracks);
            var outcome = active.Completion.Task.IsCompleted
                ? await active.Completion.Task
                : AnimationOutcome.Completed;
            return new AnimationPlanPlayResult
            {
                Outcome = outcome,
                TrackOutcomes = plan.Tracks.Select((track, index) => (track, outcome: outcomes[index]))
                    .ToDictionary(item => $"{item.track.HandleId}:{item.track.Property}", item => item.outcome, StringComparer.Ordinal)
            };
        }
        finally
        {
            lock (_animationGate)
            {
                _activePlans.Remove(active);
                if (_activePlaybacks.TryGetValue(plan.PlaybackHandleId, out var playback) && ReferenceEquals(playback, active)) _activePlaybacks.Remove(plan.PlaybackHandleId);
                foreach (var track in active.Tracks)
                {
                    if (_activePlanTracks.TryGetValue(track.Key, out var current) && ReferenceEquals(current, track)) _activePlanTracks.Remove(track.Key);
                    _activeAdditivePlanTracks.Remove(track);
                }
            }
        }
    }

    public bool CompleteAnimationImmediately(string playbackHandleId)
    {
        lock (_animationGate)
        {
            if (!_activePlaybacks.TryGetValue(playbackHandleId, out var playback)) return false;
            playback.Complete(AnimationOutcome.Skipped);
            return true;
        }
    }

    public bool SkipAnimationBatch()
    {
        lock (_animationGate)
        {
            var candidate = _activePlans.Where(plan => plan.Plan.Skippable).Cast<ISkippableAnimation>()
                .Concat(_activeAnimations.Values.Where(animation => animation.Request.Skippable))
                .Concat(_activeAdditiveAnimations.Where(animation => animation.Request.Skippable))
                .OrderBy(animation => animation.Sequence)
                .FirstOrDefault();
            if (candidate is null) return false;

            var batch = candidate.BatchKey;
            foreach (var plan in _activePlans.Where(plan => plan.Plan.Skippable && plan.BatchKey == batch).ToArray())
                plan.Complete(AnimationOutcome.Skipped);
            foreach (var animation in _activeAnimations.Values.Where(animation => animation.Request.Skippable && animation.BatchKey == batch).ToArray())
                animation.Complete(AnimationOutcome.Skipped);
            foreach (var animation in _activeAdditiveAnimations.Where(animation => animation.Request.Skippable && animation.BatchKey == batch).ToArray())
                animation.Complete(AnimationOutcome.Skipped);
            return true;
        }
    }
    public void ShowDialogue() => OnUi(() => _state.IsDialogueVisible = true);
    public void HideDialogue() => OnUi(() => _state.IsDialogueVisible = false);

    public async Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        await OnUiAsync(async () =>
        {
            if (_state.IsNvlMode)
            {
                _state.PresentNvlLine(speaker, text);
                return;
            }

            _state.IsDialogueVisible = true;
            _page.Dialogue.Speaker = speaker;
            _page.Dialogue.Text = text;
            _page.Dialogue.CharactersPerSecond = _state.TextSpeed;
            _isTyping = true;
            try { await _page.Dialogue.StartAsync(ct); }
            finally { _isTyping = false; }
        });
    }

    public void SkipTypewriter(string widgetInstanceId) => OnUi(() => _page.Dialogue.Skip());
    public void SetVoice(string assetId) => OnUi(() => _state.StatusMessage = $"Voice requested: {assetId}");
    public Task WaitForClickAsync(CancellationToken ct) => OnUiAsync(() => _state.WaitForAdvanceAsync(ct));
    public Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct) => OnUiAsync(() => _state.WaitForChoiceAsync(options, ct));

    private void Advance()
    {
        if (_isTyping)
        {
            _page.Dialogue.Skip();
            return;
        }

        if (SkipAnimationBatch()) return;

        _state.CompleteAdvance();
    }

    public void Dispose() => _state.AdvanceRequested -= Advance;

    private Task SetAnimationValueAsync(AnimationRequest request, ActiveAnimation active, double value) =>
        OnUiAsync(() =>
        {
            if (request.BlendMode == AnimationBlendMode.Additive)
                ApplyAdditiveValue(active, request.HandleId, request.Property, value);
            else
                ApplyReplaceValue(request.HandleId, request.Property, value);
            return Task.CompletedTask;
        });

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private async Task<AnimationOutcome> RunPlanTrackAsync(ActivePlan plan, AnimationTrackDefinition track, Stopwatch clock, CancellationToken ct)
    {
        var activeTrack = new ActivePlanTrack(track);
        lock (_animationGate)
        {
            if (track.BlendMode == AnimationBlendMode.Additive)
                _activeAdditivePlanTracks.Add(activeTrack);
            else
            {
                if (_activeAnimations.Remove(activeTrack.Key, out var animation)) animation.Complete(AnimationOutcome.Replaced);
                if (_activePlanTracks.Remove(activeTrack.Key, out var replaced)) replaced.Complete(AnimationOutcome.Replaced);
                _activePlanTracks.Add(activeTrack.Key, activeTrack);
            }
            plan.Tracks.Add(activeTrack);
        }

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (activeTrack.Completion.Task.IsCompleted) return await activeTrack.Completion.Task;
                if (plan.Completion.Task.IsCompleted)
                {
                    var outcome = await plan.Completion.Task;
                    if (outcome == AnimationOutcome.Skipped)
                        await SetAnimationValueAsync(activeTrack, AnimationTrackSampler.Evaluate(track, plan.Plan.DurationFrames));
                    return outcome;
                }

                var frame = Math.Min(plan.Plan.DurationFrames, clock.Elapsed.TotalSeconds * plan.Plan.FrameRate);
                await SetAnimationValueAsync(activeTrack, AnimationTrackSampler.Evaluate(track, frame));
                if (frame >= plan.Plan.DurationFrames) return AnimationOutcome.Completed;
                await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(16), ct), activeTrack.Completion.Task, plan.Completion.Task);
            }
        }
        finally
        {
            if (track.BlendMode == AnimationBlendMode.Additive)
                await OnUiAsync(() =>
                {
                    if (plan.Plan.LoopMode == AnimationLoopMode.Loop)
                        RemoveAdditiveValue(activeTrack, track.HandleId, track.Property);
                    else
                        BakeAdditiveValue(activeTrack, track.HandleId, track.Property);
                    return Task.CompletedTask;
                });
            lock (_animationGate)
            {
                if (_activePlanTracks.TryGetValue(activeTrack.Key, out var current) && ReferenceEquals(current, activeTrack)) _activePlanTracks.Remove(activeTrack.Key);
                _activeAdditivePlanTracks.Remove(activeTrack);
            }
        }
    }

    private Task SetAnimationValueAsync(ActivePlanTrack track, double value) =>
        OnUiAsync(() =>
        {
            if (track.Track.BlendMode == AnimationBlendMode.Additive)
                ApplyAdditiveValue(track, track.Track.HandleId, track.Track.Property, value);
            else
                ApplyReplaceValue(track.Track.HandleId, track.Track.Property, value);
            return Task.CompletedTask;
        });

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
        SetAnimationValue(handleId, property, ClampPresentationValue(handleId, property, baseValue + GetAdditiveContribution(handleId, property)));
    }

    private void ApplyActiveAdditiveValues(string handleId, string property)
    {
        var key = PropertyKey(handleId, property);
        if (TryGetAdditiveBaseValue(key, handleId, property, out var baseValue))
            SetAnimationValue(handleId, property, ClampPresentationValue(handleId, property, baseValue + GetAdditiveContribution(handleId, property)));
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
        lock (_animationGate)
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
        _state.TryGetEffectAnimationValue(handleId, property, out value);

    private bool SetAnimationValue(string handleId, string property, double value) =>
        _state.SetLayerAnimationValue(handleId, property, value) ||
        _state.SetEffectAnimationValue(handleId, property, value);

    private bool IsLayerAnimationValue(string handleId, string property) => _state.TryGetLayerAnimationValue(handleId, property, out _);

    private double ClampPresentationValue(string handleId, string property, double value) => IsLayerAnimationValue(handleId, property) ? ClampLayerValue(property, value) : value;

    private static double ClampLayerValue(string property, double value) => property switch
    {
        "opacity" => Math.Clamp(value, 0, 1),
        "transform.scaleX" or "transform.scaleY" => Math.Max(.001, value),
        _ => value
    };

    private interface ISkippableAnimation
    {
        long Sequence { get; }
        string BatchKey { get; }
    }

    private interface ICompletablePlayback
    {
        void Complete(AnimationOutcome outcome);
    }

    private interface IAdditiveAnimation
    {
        double CurrentValue { get; set; }
    }

    private sealed class ActiveAnimation(AnimationRequest request, long sequence) : ISkippableAnimation, ICompletablePlayback, IAdditiveAnimation
    {
        public AnimationRequest Request { get; } = request;
        public long Sequence { get; } = sequence;
        public string BatchKey { get; } = request.BatchId ?? Guid.NewGuid().ToString("N");
        public double CurrentValue { get; set; }
        public TaskCompletionSource<AnimationOutcome> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(AnimationOutcome outcome) => Outcome.TrySetResult(outcome);
    }

    private sealed class ActivePlan(AnimationPlanDefinition plan, long sequence) : ISkippableAnimation, ICompletablePlayback
    {
        public AnimationPlanDefinition Plan { get; } = plan;
        public long Sequence { get; } = sequence;
        public string BatchKey { get; } = plan.BatchId ?? Guid.NewGuid().ToString("N");
        public List<ActivePlanTrack> Tracks { get; } = [];
        public TaskCompletionSource<AnimationOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(AnimationOutcome outcome) => Completion.TrySetResult(outcome);
    }

    private sealed class ActivePlanTrack(AnimationTrackDefinition track) : IAdditiveAnimation
    {
        public AnimationTrackDefinition Track { get; } = track;
        public string Key { get; } = $"{track.HandleId}:{track.Property}";
        public double CurrentValue { get; set; }
        public TaskCompletionSource<AnimationOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(AnimationOutcome outcome) => Completion.TrySetResult(outcome);
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private static Task OnUiAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return action();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    private static Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return action();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { completion.TrySetResult(await action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }
}
