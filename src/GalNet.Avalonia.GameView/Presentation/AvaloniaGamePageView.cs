using Avalonia.Controls;
using Avalonia.Threading;
using System.Diagnostics;
using GalNet.Avalonia.GameView.Page;
using GalNet.Core.View;
using GalNet.Core.Scene;
using GalNet.Rendering.Scene;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Host-provided creation of layer visuals; the shared page never resolves files itself.</summary>
public interface IGamePageLayerFactory : ISceneTextureResolver
{
    /// <summary>Resolves a content asset ID to the immutable image texture displayed for a layer.</summary>
    /// <param name="assetId">Host-defined asset identifier from a Layer request.</param>
}

/// <summary>Maps runtime layer, dialogue and interaction ports onto a shared <see cref="GamePage"/>.</summary>
public sealed class AvaloniaGamePageView : IDisposable
{
    private readonly TaskCompletionSource _initialPresentationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    private readonly Dictionary<string, ParticleEmitter> _particleEmitters = new(StringComparer.Ordinal);

    public AvaloniaGamePageView(GamePageViewModel state, GamePage page, IGamePageLayerFactory layers)
    {
        _state = state;
        _page = page;
        _layers = layers;
        _state.AdvanceRequested += Advance;
    }

    /// <summary>Completes when the first scene has reached its initial interaction boundary.</summary>
    public Task InitialPresentationReady => _initialPresentationReady.Task;

    public void CompleteInitialPresentation() => _initialPresentationReady.TrySetResult();

    public void FailInitialPresentation(Exception exception) => _initialPresentationReady.TrySetException(exception);

    public void ShowLayer(LayerRenderRequest request) => OnUi(() =>
        _state.SetLayer(request.HandleId, new SceneLayerItem
        {
            HandleId = request.HandleId,
            Texture = request.Color is null ? _layers.ResolveTexture(request.AssetId) : null,
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

    public void ReplaceLayer(string handleId, string assetId) => OnUi(() => _state.ReplaceLayer(handleId, _layers.ResolveTexture(assetId)));
    public void HideLayer(string handleId) => OnUi(() => _state.HideLayer(handleId));
    public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec) => OnUi(() => _state.MoveLayer(handleId, transform, z));
    public Task StartParticleEmitterAsync(ParticleEmitterRequest request, CancellationToken ct) => OnUiAsync(() =>
    {
        StopParticleEmitter(request.InstanceId);
        var emitter = new ParticleEmitter(request.InstanceId, _layers.ResolveTexture(request.Definition.ParticleTexture), request.Definition, request.Z);
        emitter.Drained += OnParticleDrained;
        _particleEmitters.Add(request.InstanceId, emitter);
        _state.SceneRenderables.Add(emitter);
        RegisterParticleAnimation(request, "emissionRate", value => emitter.EmissionRate = (float)value, emitter.EmissionRate);
        RegisterParticleAnimation(request, "initialVelocityX", value => emitter.InitialVelocityX = (float)value, emitter.InitialVelocityX);
        RegisterParticleAnimation(request, "initialVelocityY", value => emitter.InitialVelocityY = (float)value, emitter.InitialVelocityY);
        RegisterParticleAnimation(request, "noise", value => emitter.Noise = (float)value, emitter.Noise);
        RegisterParticleAnimation(request, "particleScale", value => emitter.ParticleScale = (float)value, emitter.ParticleScale);
        RegisterParticleAnimation(request, "particleLifetime", value => emitter.ParticleLifetime = (float)value, emitter.ParticleLifetime);
        return Task.CompletedTask;
    });
    public Task StopParticleEmitterAsync(string instanceId, CancellationToken ct) => OnUiAsync(() => { if (_particleEmitters.TryGetValue(instanceId, out var emitter)) emitter.StopEmission(); return Task.CompletedTask; });
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
                // Keep the sampler ahead of the compositor: a 16ms task timer plus dispatcher
                // latency commonly turned a nominal 60fps movement into a 30fps cadence.
                await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(8), ct), active.Outcome.Task);
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
            var tracks = RegisterPlanTracks(active, plan.Tracks);
            AnimationOutcome outcome;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (active.Completion.Task.IsCompleted)
                {
                    outcome = await active.Completion.Task;
                    if (outcome == AnimationOutcome.Skipped)
                        await ApplyPlanFrameAsync(tracks, plan.DurationFrames);
                    break;
                }

                var frame = Math.Min(plan.DurationFrames, started.Elapsed.TotalSeconds * plan.FrameRate);
                await ApplyPlanFrameAsync(tracks, frame);
                if (frame >= plan.DurationFrames)
                {
                    outcome = AnimationOutcome.Completed;
                    break;
                }

                // A Plan samples all tracks from one clock and submits them in one UI dispatch.
                // The former per-track timers visibly jittered multi-track transitions.
                await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(8), ct), active.Completion.Task);
            }

            var outcomes = tracks.Select(track => track.Completion.Task.IsCompleted
                ? track.Completion.Task.GetAwaiter().GetResult()
                : outcome).ToArray();
            return new AnimationPlanPlayResult
            {
                Outcome = outcome,
                TrackOutcomes = plan.Tracks.Select((track, index) => (track, outcome: outcomes[index]))
                    .ToDictionary(item => $"{item.track.HandleId}:{item.track.Property}", item => item.outcome, StringComparer.Ordinal)
            };
        }
        finally
        {
            await CleanupPlanTracksAsync(active);
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
    public Task WaitForClickAsync(CancellationToken ct) => OnUiAsync(() =>
    {
        var wait = _state.WaitForAdvanceAsync(ct);
        _initialPresentationReady.TrySetResult();
        return wait;
    });

    public Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct) => OnUiAsync(() =>
    {
        var wait = _state.WaitForChoiceAsync(options, ct);
        _initialPresentationReady.TrySetResult();
        return wait;
    });

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

    public void Dispose()
    {
        _state.AdvanceRequested -= Advance;
        foreach (var id in _particleEmitters.Keys.ToArray()) StopParticleEmitter(id);
    }

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

    private IReadOnlyList<ActivePlanTrack> RegisterPlanTracks(ActivePlan plan, IEnumerable<AnimationTrackDefinition> tracks)
    {
        var registered = new List<ActivePlanTrack>();
        lock (_animationGate)
        {
            foreach (var track in tracks)
            {
                var activeTrack = new ActivePlanTrack(track);
                if (track.BlendMode == AnimationBlendMode.Additive)
                    _activeAdditivePlanTracks.Add(activeTrack);
                else
                {
                    if (_activeAnimations.Remove(activeTrack.Key, out var animation)) animation.Complete(AnimationOutcome.Replaced);
                    if (_activePlanTracks.Remove(activeTrack.Key, out var replaced)) replaced.Complete(AnimationOutcome.Replaced);
                    _activePlanTracks.Add(activeTrack.Key, activeTrack);
                }
                plan.Tracks.Add(activeTrack);
                registered.Add(activeTrack);
            }
        }
        return registered;
    }

    private Task ApplyPlanFrameAsync(IReadOnlyList<ActivePlanTrack> tracks, double frame) =>
        OnUiAsync(() =>
        {
            foreach (var activeTrack in tracks)
            {
                if (activeTrack.Completion.Task.IsCompleted) continue;
                var value = AnimationTrackSampler.Evaluate(activeTrack.Track, frame);
                if (activeTrack.Track.BlendMode == AnimationBlendMode.Additive)
                    ApplyAdditiveValue(activeTrack, activeTrack.Track.HandleId, activeTrack.Track.Property, value);
                else
                    ApplyReplaceValue(activeTrack.Track.HandleId, activeTrack.Track.Property, value);
            }
            return Task.CompletedTask;
        });

    private async Task CleanupPlanTracksAsync(ActivePlan plan)
    {
        await OnUiAsync(() =>
        {
            foreach (var activeTrack in plan.Tracks.Where(track => track.Track.BlendMode == AnimationBlendMode.Additive))
            {
                if (plan.Plan.LoopMode == AnimationLoopMode.Loop)
                    RemoveAdditiveValue(activeTrack, activeTrack.Track.HandleId, activeTrack.Track.Property);
                else
                    BakeAdditiveValue(activeTrack, activeTrack.Track.HandleId, activeTrack.Track.Property);
            }
            return Task.CompletedTask;
        });
        lock (_animationGate)
        {
            _activePlans.Remove(plan);
            if (_activePlaybacks.TryGetValue(plan.Plan.PlaybackHandleId, out var playback) && ReferenceEquals(playback, plan)) _activePlaybacks.Remove(plan.Plan.PlaybackHandleId);
            foreach (var track in plan.Tracks)
            {
                if (_activePlanTracks.TryGetValue(track.Key, out var current) && ReferenceEquals(current, track)) _activePlanTracks.Remove(track.Key);
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
        _state.TryGetEffectAnimationValue(handleId, property, out value) ||
        _state.TryGetParticleAnimationValue(handleId, property, out value);

    private bool SetAnimationValue(string handleId, string property, double value) =>
        _state.SetLayerAnimationValue(handleId, property, value) ||
        _state.SetEffectAnimationValue(handleId, property, value) ||
        _state.SetParticleAnimationValue(handleId, property, value);

    private void RegisterParticleAnimation(ParticleEmitterRequest request, string property, Action<double> apply, double fallback)
    {
        var initial = request.AnimationValues.TryGetValue(property, out var value) ? value : fallback;
        _state.RegisterParticleAnimation(request.InstanceId, property, apply, initial);
    }
    // The frame host may be enumerating SceneRenderables when drain completes; remove on the
    // following UI turn so collection mutation cannot invalidate that enumeration.
    private void OnParticleDrained(ParticleEmitter emitter) => Dispatcher.UIThread.Post(() => StopParticleEmitter(emitter.HandleId));
    private void StopParticleEmitter(string instanceId)
    {
        if (!_particleEmitters.Remove(instanceId, out var emitter)) return;
        emitter.Drained -= OnParticleDrained;
        _state.UnregisterParticleAnimations(instanceId);
        _state.SceneRenderables.Remove(emitter);
        emitter.Dispose();
    }

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

    private void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            action();
        });
    }

    private Task OnUiAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return action();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await action();
                completion.TrySetResult();
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    private Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return action();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await action());
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }
}
