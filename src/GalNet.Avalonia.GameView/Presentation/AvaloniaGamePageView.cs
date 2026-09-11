using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using GalNet.Avalonia.GameView.Page;
using GalNet.Core.View;
using GalNet.Core.Scene;
using GalNet.Game.Controls;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Host-provided creation of layer visuals; the shared page never resolves files itself.</summary>
public interface IGamePageLayerFactory
{
    /// <summary>Resolves a content asset ID to the Avalonia image displayed for a layer.</summary>
    /// <param name="assetId">Host-defined asset identifier from a Layer request.</param>
    /// <returns>The image to display, or <see langword="null"/> when the host cannot resolve the asset.</returns>
    IImage? ResolveLayerImage(string assetId);
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
            if (_activePlanTracks.Remove(key, out var planTrack)) planTrack.Complete(AnimationOutcome.Replaced);
            if (_activeAnimations.Remove(key, out var replaced)) replaced.Complete(AnimationOutcome.Replaced);
            _activeAnimations.Add(key, active);
            _activePlaybacks.Add(request.PlaybackHandleId, active);
        }

        try
        {
            var from = request.From ?? (float)await OnUiAsync(() =>
                Task.FromResult(_state.TryGetLayerAnimationValue(request.HandleId, request.Property, out var value) ? value : double.NaN));
            if (double.IsNaN(from)) return AnimationOutcome.Replaced;
            if (request.From.HasValue) await SetAnimationValueAsync(request, from);

            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (active.Outcome.Task.IsCompleted) return await active.Outcome.Task;
                var progress = request.DurationSeconds <= 0 ? 1 : Math.Min(1, stopwatch.Elapsed.TotalSeconds / request.DurationSeconds);
                await SetAnimationValueAsync(request, Lerp(from, request.To, request.Curve.Evaluate((float)progress)));
                if (progress >= 1) return AnimationOutcome.Completed;
                await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(16), ct), active.Outcome.Task);
            }
        }
        finally
        {
            lock (_animationGate)
            {
                if (_activeAnimations.TryGetValue(key, out var current) && ReferenceEquals(current, active)) _activeAnimations.Remove(key);
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
                    if (_activePlanTracks.TryGetValue(track.Key, out var current) && ReferenceEquals(current, track)) _activePlanTracks.Remove(track.Key);
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
                .OrderBy(animation => animation.Sequence)
                .FirstOrDefault();
            if (candidate is null) return false;

            var batch = candidate.BatchKey;
            foreach (var plan in _activePlans.Where(plan => plan.Plan.Skippable && plan.BatchKey == batch).ToArray())
                plan.Complete(AnimationOutcome.Skipped);
            foreach (var animation in _activeAnimations.Values.Where(animation => animation.Request.Skippable && animation.BatchKey == batch).ToArray())
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

    private Task SetAnimationValueAsync(AnimationRequest request, double value) =>
        OnUiAsync(() =>
        {
            _state.SetLayerAnimationValue(request.HandleId, request.Property, value);
            return Task.CompletedTask;
        });

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private async Task<AnimationOutcome> RunPlanTrackAsync(ActivePlan plan, AnimationTrackDefinition track, Stopwatch clock, CancellationToken ct)
    {
        var activeTrack = new ActivePlanTrack(track);
        lock (_animationGate)
        {
            if (_activeAnimations.Remove(activeTrack.Key, out var animation)) animation.Complete(AnimationOutcome.Replaced);
            if (_activePlanTracks.Remove(activeTrack.Key, out var replaced)) replaced.Complete(AnimationOutcome.Replaced);
            _activePlanTracks.Add(activeTrack.Key, activeTrack);
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
                        await SetAnimationValueAsync(track.HandleId, track.Property, AnimationTrackSampler.Evaluate(track, plan.Plan.DurationFrames));
                    return outcome;
                }

                var frame = Math.Min(plan.Plan.DurationFrames, clock.Elapsed.TotalSeconds * plan.Plan.FrameRate);
                await SetAnimationValueAsync(track.HandleId, track.Property, AnimationTrackSampler.Evaluate(track, frame));
                if (frame >= plan.Plan.DurationFrames) return AnimationOutcome.Completed;
                await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(16), ct), activeTrack.Completion.Task, plan.Completion.Task);
            }
        }
        finally
        {
            lock (_animationGate)
                if (_activePlanTracks.TryGetValue(activeTrack.Key, out var current) && ReferenceEquals(current, activeTrack)) _activePlanTracks.Remove(activeTrack.Key);
        }
    }

    private Task SetAnimationValueAsync(string handleId, string property, double value) =>
        OnUiAsync(() =>
        {
            _state.SetLayerAnimationValue(handleId, property, value);
            return Task.CompletedTask;
        });

    private interface ISkippableAnimation
    {
        long Sequence { get; }
        string BatchKey { get; }
    }

    private interface ICompletablePlayback
    {
        void Complete(AnimationOutcome outcome);
    }

    private sealed class ActiveAnimation(AnimationRequest request, long sequence) : ISkippableAnimation, ICompletablePlayback
    {
        public AnimationRequest Request { get; } = request;
        public long Sequence { get; } = sequence;
        public string BatchKey { get; } = request.BatchId ?? Guid.NewGuid().ToString("N");
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

    private sealed class ActivePlanTrack(AnimationTrackDefinition track)
    {
        public string Key { get; } = $"{track.HandleId}:{track.Property}";
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
