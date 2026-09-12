using GalNet.Core.Entry;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.View;
using GalNet.Runtime.Logging;
using System.Text.Json;

namespace GalNet.Runtime.Handlers;

public sealed class ShowLayerHandler : EntryHandler
{
    public override string EntryType => ShowLayerEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var id = context.GetString("handleId");
        var asset = context.GetString("assetId");
        var layer = context.Runtime.SceneInstances.TryGet<Layer>(id, out var existing) ? existing : null;
        layer ??= context.GetBool("transient")
            ? context.Runtime.SceneInstances.GetOrAddTransient(id, handleId => new Layer { Id = handleId })
            : context.Runtime.SceneInstances.GetOrAdd(id, handleId => new Layer { Id = handleId });

        layer.AssetId = asset;
        layer.Flipbook = ReadFlipbook(context.GetString("flipbook"));
        layer.Color = null;
        layer.Transform = context.GetLayerTransform();
        layer.Z = context.GetFloat("z");
        layer.Opacity = context.GetFloat("opacity", 1);
        layer.DisplayMode = Enum.TryParse<LayerDisplayMode>(context.GetString("displayMode", "Native"), true, out var displayMode)
            ? displayMode
            : LayerDisplayMode.Native;
        layer.Visible = true;

        view.ShowLayer(new LayerRenderRequest(layer.Id, layer.AssetId, layer.Transform.Clone(), layer.Z, layer.DisplayMode, layer.Opacity, layer.Color, layer.Flipbook?.Clone()));
        return Task.CompletedTask;
    }

    private static FlipbookDefinition? ReadFlipbook(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "{}") return null;
        try
        {
            var flipbook = JsonSerializer.Deserialize<FlipbookDefinition>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return flipbook is { IsValid: true } ? flipbook : throw new InvalidDataException("Flipbook requires positive columns, rows and frameCount no greater than columns × rows.");
        }
        catch (JsonException exception) { throw new InvalidDataException("Invalid flipbook definition.", exception); }
    }
}

public sealed class ShowColorLayerHandler : EntryHandler
{
    public override string EntryType => ShowColorLayerEntry.TypeId;

    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var color = context.GetString("color");
        if (!Layer.IsValidColor(color))
            throw new InvalidDataException("Color layers require a #RRGGBB or #AARRGGBB color.");

        var layer = context.Runtime.SceneInstances.GetOrAddTransient(context.GetString("handleId"), id => new Layer { Id = id });
        layer.AssetId = "";
        layer.Flipbook = null;
        layer.Color = color;
        layer.Transform = context.GetLayerTransform();
        layer.Z = context.GetFloat("z", 1000);
        layer.Opacity = context.GetFloat("opacity", 1);
        layer.DisplayMode = LayerDisplayMode.Fill;
        layer.Visible = true;
        view.ShowLayer(new LayerRenderRequest(layer.Id, layer.AssetId, layer.Transform.Clone(), layer.Z, layer.DisplayMode, layer.Opacity, layer.Color));
        return Task.CompletedTask;
    }
}

public sealed class HideLayerHandler : EntryHandler
{
    public override string EntryType => HideLayerEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var id = context.GetString("handleId");
        if (!context.Runtime.SceneInstances.Remove<Layer>(id, out _))
        {
            GameLog.Logger.Warning("Layer hide ignored because handle '{HandleId}' is not active.", id);
            return Task.CompletedTask;
        }

        view.HideLayer(id);
        return Task.CompletedTask;
    }
}

public sealed class MoveLayerHandler : EntryHandler
{
    public override string EntryType => MoveLayerEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var id = context.GetString("handleId");
        var transform = context.GetLayerTransform();
        if (!context.Runtime.SceneInstances.TryGet<Layer>(id, out var layer))
        {
            GameLog.Logger.Warning("Layer move ignored because handle '{HandleId}' is not active.", id);
            return Task.CompletedTask;
        }

        layer.Transform = transform;
        layer.Z = context.GetFloat("z");

        view.MoveLayer(id, transform, context.GetFloat("z"), context.GetFloat("duration", 0.5f));
        return Task.CompletedTask;
    }
}

public sealed class ReplaceLayerHandler : EntryHandler
{
    public override string EntryType => ReplaceLayerEntry.TypeId;

    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var handleId = context.GetString("handleId");
        var assetId = context.GetString("assetId");
        if (!context.Runtime.SceneInstances.TryGet<Layer>(handleId, out var layer))
        {
            GameLog.Logger.Warning("Layer replace ignored because handle '{HandleId}' is not active.", handleId);
            return Task.CompletedTask;
        }

        layer.AssetId = assetId;
        layer.Flipbook = null;
        layer.Color = null;
        view.ReplaceLayer(handleId, assetId);
        return Task.CompletedTask;
    }
}

public sealed class AnimateHandler : EntryHandler
{
    public override string EntryType => AnimateEntry.TypeId;

    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var request = context.GetAnimation();
        if (context.Runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(request.PlaybackHandleId, out _))
        {
            GameLog.Logger.Warning("Animate ignored because playback handle '{PlaybackHandleId}' is already active.", request.PlaybackHandleId);
            return;
        }
        if (!context.Runtime.SceneInstances.TryGet<AnimatableSceneInstance>(request.HandleId, out var instance))
        {
            GameLog.Logger.Warning("Animate ignored because handle '{HandleId}' is not an active animatable instance.", request.HandleId);
            return;
        }
        var property = instance is EffectInstance effectInstance
            ? effectInstance.EnsureAnimationProperty(request.Property)
            : instance.AnimatableProperties.FirstOrDefault(item => item.Name == request.Property);
        if (property is null ||
            (request.BlendMode == AnimationBlendMode.Replace &&
             (!property.Accepts(request.To) || (request.From is { } from && !property.Accepts(from)))))
        {
            GameLog.Logger.Warning("Animate ignored because '{Property}' does not accept the supplied value.", request.Property);
            return;
        }

        AnimationPlaybackInstance playbackInstance;
        try
        {
            playbackInstance = context.Runtime.SceneInstances.GetOrAdd(request.PlaybackHandleId,
                id => new AnimationPlaybackInstance { Id = id, LoopMode = request.LoopMode });
        }
        catch (InvalidOperationException exception)
        {
            GameLog.Logger.Warning(exception, "Animate ignored because playback handle '{PlaybackHandleId}' belongs to another scene instance type.", request.PlaybackHandleId);
            return;
        }

        PersistLoop(context, AnimateEntry.TypeId, request.PlaybackHandleId, request.LoopMode);

        async Task RunAsync()
        {
            try
            {
                AnimationOutcome outcome;
                do
                {
                    outcome = await view.AnimateAsync(request, ct);
                    if (request.LoopMode == AnimationLoopMode.Once || playbackInstance.RequestedStop is not null || outcome != AnimationOutcome.Completed)
                        break;
                } while (true);

                if ((outcome is AnimationOutcome.Completed or AnimationOutcome.Skipped) &&
                    !(request.LoopMode != AnimationLoopMode.Once && request.BlendMode == AnimationBlendMode.Additive) &&
                    context.Runtime.SceneInstances.TryGet<AnimatableSceneInstance>(request.HandleId, out var current) &&
                    ReferenceEquals(current, instance))
                {
                    if (!instance.TrySetAnimationValue(request.Property, GetCommittedValue(instance, property, request.Property, request.To, request.BlendMode), out var error))
                        GameLog.Logger.Warning("Animation completion could not set '{Property}' on '{HandleId}': {Error}", request.Property, request.HandleId, error);
                    else if (instance is EffectInstance effect)
                        EffectStatePersistence.PersistAnimationValues(context.Runtime, effect);
                }
            }
            finally
            {
                RemovePersistedLoop(context.Runtime, request.PlaybackHandleId);
                context.Runtime.SceneInstances.Remove<AnimationPlaybackInstance>(request.PlaybackHandleId, out _);
            }
        }

        if (request.Blocking)
        {
            await RunAsync();
            return;
        }

        _ = RunAsync().ContinueWith(
            task => GameLog.Logger.Error(task.Exception, "Non-blocking animation failed"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static float GetCommittedValue(
        AnimatableSceneInstance instance,
        AnimatableProperty property,
        string propertyName,
        float animationValue,
        AnimationBlendMode blendMode)
    {
        if (blendMode == AnimationBlendMode.Replace)
            return animationValue;

        if (!instance.TryGetAnimationValue(propertyName, out var baseValue))
            return animationValue;

        var value = baseValue + animationValue;
        return property.Minimum is { } minimum && value < minimum ? minimum
            : property.Maximum is { } maximum && value > maximum ? maximum
            : value;
    }

    private static void PersistLoop(EntryContext context, string entryType, string playbackHandleId, AnimationLoopMode loopMode)
    {
        if (loopMode != AnimationLoopMode.Loop) return;
        context.Runtime.SceneState.ActiveAnimations.RemoveAll(animation => animation.PlaybackHandleId == playbackHandleId);
        context.Runtime.SceneState.ActiveAnimations.Add(new ActiveAnimationState
        {
            EntryType = entryType,
            PlaybackHandleId = playbackHandleId,
            Parameters = context.Entry.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        });
    }

    private static void RemovePersistedLoop(IGameRuntime runtime, string playbackHandleId) =>
        runtime.SceneState.ActiveAnimations.RemoveAll(animation => animation.PlaybackHandleId == playbackHandleId);
}

public sealed class StopAnimationHandler : EntryHandler
{
    public override string EntryType => StopAnimationEntry.TypeId;

    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var playbackHandleId = context.GetString("playbackHandleId");
        if (!Enum.TryParse<AnimationStopMode>(context.GetString("mode", "AfterIteration"), true, out var mode) || !Enum.IsDefined(mode))
            throw new InvalidDataException($"Unknown animation stop mode '{context.GetString("mode")}'.");
        if (!context.Runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(playbackHandleId, out var playback))
        {
            GameLog.Logger.Warning("Animation stop ignored because playback handle '{PlaybackHandleId}' is not active.", playbackHandleId);
            return Task.CompletedTask;
        }

        playback.RequestStop(mode);
        if (mode == AnimationStopMode.CompleteImmediately && !view.CompleteAnimationImmediately(playbackHandleId))
            GameLog.Logger.Warning("Animation stop could not complete playback handle '{PlaybackHandleId}' because it is not currently rendering.", playbackHandleId);
        return Task.CompletedTask;
    }
}

public sealed class PlayAnimationPlanHandler : EntryHandler
{
    public override string EntryType => PlayAnimationPlanEntry.TypeId;

    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var plan = context.GetAnimationPlan();
        if (context.Runtime.SceneInstances.TryGet<AnimationPlaybackInstance>(plan.PlaybackHandleId, out _))
        {
            GameLog.Logger.Warning("Animation plan ignored because playback handle '{PlaybackHandleId}' is already active.", plan.PlaybackHandleId);
            return;
        }
        AnimationPlaybackInstance playbackInstance;
        try
        {
            playbackInstance = context.Runtime.SceneInstances.GetOrAdd(plan.PlaybackHandleId,
                id => new AnimationPlaybackInstance { Id = id, LoopMode = plan.LoopMode });
        }
        catch (InvalidOperationException exception)
        {
            GameLog.Logger.Warning(exception, "Animation plan ignored because playback handle '{PlaybackHandleId}' belongs to another scene instance type.", plan.PlaybackHandleId);
            return;
        }

        PersistLoop(context, PlayAnimationPlanEntry.TypeId, plan.PlaybackHandleId, plan.LoopMode);

        async Task RunAsync()
        {
            try
            {
                var iteration = 0;
                while (true)
                {
                    var scope = plan.LoopMode == AnimationLoopMode.Loop
                        ? new AnimationIterationScope(plan.PlaybackHandleId, iteration++)
                        : null;
                    var iterationPlan = scope?.Bind(plan) ?? plan;
                    var events = new TimelineEventDispatcher(context, timeProvider, ct);
                    await events.StartAsync(iterationPlan);
                    if (!ValidateTracks(context.Runtime, iterationPlan))
                    {
                        events.Complete(iterationPlan, AnimationOutcome.Replaced);
                        return;
                    }

                    var result = await view.PlayAnimationPlanAsync(iterationPlan, ct);
                    events.Complete(iterationPlan, result.Outcome);
                    CommitStableTracks(context.Runtime, iterationPlan, result);
                    scope?.Cleanup(context.Runtime, view);

                    if (plan.LoopMode != AnimationLoopMode.Loop || playbackInstance.RequestedStop is not null ||
                        result.Outcome != AnimationOutcome.Completed)
                        return;
                }
            }
            finally
            {
                RemovePersistedLoop(context.Runtime, plan.PlaybackHandleId);
                context.Runtime.SceneInstances.Remove<AnimationPlaybackInstance>(plan.PlaybackHandleId, out _);
            }
        }

        if (plan.Blocking)
        {
            await RunAsync();
            return;
        }

        _ = RunAsync().ContinueWith(
            task => GameLog.Logger.Error(task.Exception, "Non-blocking animation plan failed"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static bool ValidateTracks(IGameRuntime runtime, AnimationPlanDefinition plan)
    {
        foreach (var track in plan.Tracks)
        {
            if (!runtime.SceneInstances.TryGet<AnimatableSceneInstance>(track.HandleId, out var instance))
            {
                GameLog.Logger.Warning("Animation plan ignored because handle '{HandleId}' is not active.", track.HandleId);
                return false;
            }

            var property = instance is EffectInstance effectInstance
                ? effectInstance.EnsureAnimationProperty(track.Property)
                : instance.AnimatableProperties.FirstOrDefault(candidate => candidate.Name == track.Property);
            if (property is null ||
                (track.BlendMode == AnimationBlendMode.Replace && track.Keys.Any(key => !property.Accepts(key.Value))))
            {
                GameLog.Logger.Warning("Animation plan ignored because '{HandleId}.{Property}' has an unsupported key value.", track.HandleId, track.Property);
                return false;
            }
        }
        return true;
    }

    private static void CommitStableTracks(IGameRuntime runtime, AnimationPlanDefinition plan, AnimationPlanPlayResult result)
    {
        foreach (var track in plan.Tracks)
        {
            var key = $"{track.HandleId}:{track.Property}";
            if (!result.TrackOutcomes.TryGetValue(key, out var outcome) || outcome is not (AnimationOutcome.Completed or AnimationOutcome.Skipped) ||
                (plan.LoopMode == AnimationLoopMode.Loop && track.BlendMode == AnimationBlendMode.Additive))
                continue;
            if (runtime.SceneInstances.TryGet<AnimatableSceneInstance>(track.HandleId, out var instance))
            {
                if (!instance.TrySetAnimationValue(track.Property, GetCommittedValue(instance, instance.AnimatableProperties.First(candidate => candidate.Name == track.Property), track.Property, track.Keys[^1].Value, track.BlendMode), out var error))
                    GameLog.Logger.Warning("Animation plan completion could not set '{HandleId}.{Property}': {Error}", track.HandleId, track.Property, error);
                else if (instance is EffectInstance effect)
                    EffectStatePersistence.PersistAnimationValues(runtime, effect);
            }
        }
    }

    private static float GetCommittedValue(
        AnimatableSceneInstance instance,
        AnimatableProperty property,
        string propertyName,
        float animationValue,
        AnimationBlendMode blendMode)
    {
        if (blendMode == AnimationBlendMode.Replace)
            return animationValue;

        if (!instance.TryGetAnimationValue(propertyName, out var baseValue))
            return animationValue;

        var value = baseValue + animationValue;
        return property.Minimum is { } minimum && value < minimum ? minimum
            : property.Maximum is { } maximum && value > maximum ? maximum
            : value;
    }

    private static void PersistLoop(EntryContext context, string entryType, string playbackHandleId, AnimationLoopMode loopMode)
    {
        if (loopMode != AnimationLoopMode.Loop) return;
        context.Runtime.SceneState.ActiveAnimations.RemoveAll(animation => animation.PlaybackHandleId == playbackHandleId);
        context.Runtime.SceneState.ActiveAnimations.Add(new ActiveAnimationState
        {
            EntryType = entryType,
            PlaybackHandleId = playbackHandleId,
            Parameters = context.Entry.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        });
    }

    private static void RemovePersistedLoop(IGameRuntime runtime, string playbackHandleId) =>
        runtime.SceneState.ActiveAnimations.RemoveAll(animation => animation.PlaybackHandleId == playbackHandleId);

    /// <summary>Translates Loop Plan handles into one-iteration-only internal scene handles.</summary>
    private sealed class AnimationIterationScope(string playbackHandleId, int iteration)
    {
        private readonly Dictionary<string, string> _handles = new(StringComparer.Ordinal);
        public IReadOnlySet<string> Handles => _handles.Values.ToHashSet(StringComparer.Ordinal);

        public AnimationPlanDefinition Bind(AnimationPlanDefinition source)
        {
            foreach (var track in source.Tracks) Resolve(track.HandleId);
            foreach (var timelineEvent in source.Events.Where(item => item.Type.StartsWith("layer.", StringComparison.Ordinal)))
                if (timelineEvent.Parameters.TryGetValue("handleId", out var handle)) Resolve(ToValue(handle));

            return new AnimationPlanDefinition
            {
                PlaybackHandleId = source.PlaybackHandleId,
                FrameRate = source.FrameRate,
                DurationFrames = source.DurationFrames,
                Blocking = source.Blocking,
                Skippable = source.Skippable,
                BatchId = source.BatchId,
                LoopMode = source.LoopMode,
                Tracks = source.Tracks.Select(track => new AnimationTrackDefinition
                {
                    HandleId = Resolve(track.HandleId),
                    Property = track.Property, BlendMode = track.BlendMode,
                    Keys = track.Keys.Select(key => new AnimationKeyframeDefinition
                    {
                        Frame = key.Frame, Value = key.Value, InTangent = key.InTangent,
                        OutTangent = key.OutTangent, InterpolationToNext = key.InterpolationToNext
                    }).ToList()
                }).ToList(),
                Events = source.Events.Select(timelineEvent => new AnimationPlanEventDefinition
                {
                    Frame = timelineEvent.Frame,
                    Type = timelineEvent.Type,
                    Parameters = BindParameters(timelineEvent)
                }).ToList()
            };
        }

        public void Cleanup(IGameRuntime runtime, IGameView view)
        {
            foreach (var handle in _handles.Values)
            {
                if (runtime.SceneInstances.Remove<Layer>(handle, out _)) view.HideLayer(handle);
            }
        }

        private string Resolve(string logicalHandle)
        {
            if (_handles.TryGetValue(logicalHandle, out var resolved)) return resolved;
            resolved = $"{playbackHandleId}:loop:{iteration}:{logicalHandle}";
            _handles.Add(logicalHandle, resolved);
            return resolved;
        }

        private Dictionary<string, JsonElement> BindParameters(AnimationPlanEventDefinition timelineEvent)
        {
            var parameters = timelineEvent.Parameters.ToDictionary(pair => pair.Key,
                pair => timelineEvent.Type.StartsWith("layer.", StringComparison.Ordinal) && pair.Key == "handleId"
                    ? ToJsonElement(Resolve(ToValue(pair.Value)))
                    : pair.Value.Clone(), StringComparer.Ordinal);
            if (timelineEvent.Type == ShowLayerEntry.TypeId)
                parameters["transient"] = ToJsonElement("true");
            return parameters;
        }

        private static JsonElement ToJsonElement(string value)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
            return document.RootElement.Clone();
        }

        private static string ToValue(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            JsonValueKind.Null => "",
            _ => throw new InvalidDataException("Timeline event parameters cannot be undefined.")
        };
    }

    private sealed class TimelineEventDispatcher(EntryContext context, TimeProvider timeProvider, CancellationToken outerCancellation)
    {
        private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        private readonly HashSet<AnimationPlanEventDefinition> _triggered = [];
        public async Task StartAsync(AnimationPlanDefinition plan)
        {
            foreach (var timelineEvent in plan.Events.Where(item => item.Frame == 0)) await TriggerAsync(timelineEvent);
            _ = DispatchAsync(plan);
        }

        public void Complete(AnimationPlanDefinition plan, AnimationOutcome outcome)
        {
            _cancellation.Cancel();
            if (outcome is AnimationOutcome.Completed or AnimationOutcome.Skipped)
            {
                // A playback implementation may report completion before this independent
                // scheduler wakes for its final frame (the headless view intentionally does).
                // Skipping deliberately uses the same rule: one input completes the whole
                // clip, including its cleanup.
                foreach (var timelineEvent in plan.Events.OrderBy(item => item.Frame)) Trigger(timelineEvent);
            }
        }

        private async Task DispatchAsync(AnimationPlanDefinition plan)
        {
            try
            {
                var previousFrame = 0;
                foreach (var timelineEvent in plan.Events.Where(item => item.Frame > 0).OrderBy(item => item.Frame))
                {
                    var delay = TimeSpan.FromSeconds((timelineEvent.Frame - previousFrame) / (double)plan.FrameRate);
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, timeProvider, _cancellation.Token);
                    previousFrame = timelineEvent.Frame;
                    Trigger(timelineEvent);
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        }

        private async Task TriggerAsync(AnimationPlanEventDefinition timelineEvent)
        {
            lock (_triggered)
                if (!_triggered.Add(timelineEvent)) return;

            if (context.DispatchTimelineEventAsync is null)
            {
                GameLog.Logger.Warning("Animation plan event '{EntryType}' cannot dispatch without an engine context.", timelineEvent.Type);
                return;
            }

            Entry entry;
            try
            {
                var values = timelineEvent.Parameters.ToDictionary(pair => pair.Key, pair => ToValue(pair.Value));
                entry = EntryRegistry.Create(timelineEvent.Type, values: values);
                // The loop scope injects this runtime-only marker after schema filtering, so
                // authoring UIs never expose a persistence-breaking transient toggle.
                if (timelineEvent.Type == ShowLayerEntry.TypeId && values.TryGetValue("transient", out var transient))
                    entry.Values["transient"] = transient;
            }
            catch (Exception exception)
            {
                GameLog.Logger.Warning(exception, "Animation plan event '{EntryType}' is invalid.", timelineEvent.Type);
                return;
            }

            // Timeline events are detached from the Plan's lifetime. In particular, an end
            // event must still be able to hide an instance after the Plan has been cancelled.
            await context.DispatchTimelineEventAsync(entry, CancellationToken.None);
        }

        private void Trigger(AnimationPlanEventDefinition timelineEvent) => _ = TriggerAsync(timelineEvent).ContinueWith(
                task => GameLog.Logger.Error(task.Exception, "Animation plan event '{EntryType}' failed", timelineEvent.Type),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        private static string ToValue(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            JsonValueKind.Null => "",
            _ => throw new InvalidDataException("Timeline event parameters cannot be undefined.")
        };
    }
}

public sealed class PlayAudioHandler : EntryHandler
{
    public override string EntryType => PlayAudioEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        view.PlayAudio(context.GetString("channel", "bgm"), context.GetString("asset"), context.GetFloat("volume", 0.8f), context.GetString("mode", "once"), context.GetInt("times", 1));
        return Task.CompletedTask;
    }
}

public sealed class StopAudioHandler : EntryHandler
{
    public override string EntryType => StopAudioEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        view.StopAudio(context.GetString("channel", "bgm"));
        return Task.CompletedTask;
    }
}

public sealed class PauseAudioHandler : EntryHandler
{
    public override string EntryType => PauseAudioEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        view.PauseAudio(context.GetString("channel", "bgm"));
        return Task.CompletedTask;
    }
}

public sealed class ResumeAudioHandler : EntryHandler
{
    public override string EntryType => ResumeAudioEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        view.ResumeAudio(context.GetString("channel", "bgm"));
        return Task.CompletedTask;
    }
}

public sealed class EnqueueAudioHandler : EntryHandler
{
    public override string EntryType => EnqueueAudioEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        view.EnqueueAudio(context.GetString("channel", "bgm"), context.GetString("asset"), context.GetInt("times", 1));
        return Task.CompletedTask;
    }
}

public sealed class PlayVideoHandler : EntryHandler
{
    public override string EntryType => PlayVideoEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        view.PlayVideo(context.GetString("asset"));
        return Task.CompletedTask;
    }
}

public sealed class StopVideoHandler : EntryHandler
{
    public override string EntryType => StopVideoEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        view.StopVideo();
        return Task.CompletedTask;
    }
}

public sealed class ShowDialogueHandler : EntryHandler
{
    public override string EntryType => ShowDialogueEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        view.ShowDialogue();
        return Task.CompletedTask;
    }
}

public sealed class HideDialogueHandler : EntryHandler
{
    public override string EntryType => HideDialogueEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        view.HideDialogue();
        return Task.CompletedTask;
    }
}

public sealed class ApplyEffectHandler : EntryHandler
{
    public override string EntryType => ApplyEffectEntry.TypeId;
    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var request = new EffectRequest(
            context.GetString("id"),
            context.GetString("instanceId"),
            context.GetString("targetHandleId"),
            context.GetInt("order"),
            context.GetString("parameters"));

        if (!string.IsNullOrWhiteSpace(request.InstanceId))
        {
            Layer? targetLayer = null;
            if (!string.IsNullOrWhiteSpace(request.TargetHandleId))
            {
                if (!context.Runtime.SceneInstances.TryGet<Layer>(request.TargetHandleId, out targetLayer))
                    throw new InvalidDataException($"Effect '{request.Id}' targets inactive layer '{request.TargetHandleId}'.");
            }

            if (context.Runtime.SceneInstances.TryGet<EffectInstance>(request.InstanceId, out var existing) &&
                (!string.Equals(existing.EffectId, request.Id, StringComparison.Ordinal) ||
                 !string.Equals(existing.TargetHandleId, request.TargetHandleId, StringComparison.Ordinal) ||
                 existing.Order != request.Order))
                throw new InvalidDataException($"Effect instance '{request.InstanceId}' is already active with a different definition.");

            var instance = context.Runtime.SceneInstances.GetOrAdd<EffectInstance>(request.InstanceId, id => new EffectInstance
            {
                Id = id, EffectId = request.Id, TargetHandleId = request.TargetHandleId, Order = request.Order, Parameters = request.Parameters
            });
            if (targetLayer is not null)
            {
                if (!targetLayer.EffectInstanceIds.Contains(request.InstanceId, StringComparer.Ordinal))
                    targetLayer.EffectInstanceIds.Add(request.InstanceId);
            }
            if (!context.Runtime.SceneState.ActiveEffectIds.Contains(request.InstanceId))
                context.Runtime.SceneState.ActiveEffectIds.Add(request.InstanceId);
            context.Runtime.SceneState.ActiveEffects.RemoveAll(effect => effect.InstanceId == request.InstanceId);
            context.Runtime.SceneState.ActiveEffects.Add(new ActiveEffectState
            {
                Id = request.Id,
                InstanceId = request.InstanceId,
            TargetHandleId = request.TargetHandleId,
            Order = request.Order,
                Parameters = request.Parameters,
                AnimationValues = instance.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            });
            request = request with { AnimationValues = instance.AnimationValues };
        }

        await view.StartEffectAsync(request, ct);
    }
}

internal static class EffectStatePersistence
{
    public static void PersistAnimationValues(IGameRuntime runtime, EffectInstance instance)
    {
        var state = runtime.SceneState.ActiveEffects.FirstOrDefault(effect => effect.InstanceId == instance.Id);
        if (state is null) return;
        state.AnimationValues.Clear();
        foreach (var (propertyName, value) in instance.AnimationValues)
            state.AnimationValues[propertyName] = value;
    }
}

public sealed class StopEffectHandler : EntryHandler
{
    public override string EntryType => StopEffectEntry.TypeId;
    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var instanceId = context.GetString("instanceId");
        if (context.Runtime.SceneInstances.TryGet<EffectInstance>(instanceId, out var effect) &&
            !string.IsNullOrWhiteSpace(effect.TargetHandleId) &&
            context.Runtime.SceneInstances.TryGet<Layer>(effect.TargetHandleId, out var layer))
            layer.EffectInstanceIds.Remove(instanceId);
        context.Runtime.SceneState.ActiveEffectIds.Remove(instanceId);
        context.Runtime.SceneState.ActiveEffects.RemoveAll(effect => effect.InstanceId == instanceId);
        await view.StopEffectAsync(instanceId, ct);
        context.Runtime.SceneInstances.Remove<EffectInstance>(instanceId, out _);
    }
}

public sealed class SetVariableHandler : EntryHandler
{
    public override string EntryType => SetVariableEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var target = context.GetString("target");
        var expression = context.GetString("expression");
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(expression))
        {
            Serilog.Log.ForContext("LogChannel", "Game").Warning("Set variable skipped because target or expression is empty: target={Target}", target);
            return Task.CompletedTask;
        }

        try
        {
            var result = context.Runtime.EvaluateExpression(expression);
            if (result is null)
            {
                Serilog.Log.ForContext("LogChannel", "Game").Warning("Set variable expression returned null: target={Target}, expression={Expression}", target, expression);
                return Task.CompletedTask;
            }
            context.Runtime.SetVariable(target, result);
        }
        catch (Exception exception)
        {
            Serilog.Log.ForContext("LogChannel", "Game").Warning(exception, "Set variable expression failed: target={Target}, expression={Expression}", target, expression);
        }

        return Task.CompletedTask;
    }
}
