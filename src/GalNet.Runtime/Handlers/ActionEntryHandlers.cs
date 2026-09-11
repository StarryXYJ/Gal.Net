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
    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var id = context.GetString("handleId");
        var asset = context.GetString("assetId");
        var layer = context.Runtime.SceneInstances.TryGet<Layer>(id, out var existing) ? existing : null;
        var previousAsset = layer?.AssetId;
        layer ??= context.Runtime.SceneInstances.GetOrAdd(id, handleId => new Layer { Id = handleId });

        layer.AssetId = asset;
        layer.Transform = context.GetLayerTransform();
        layer.Z = context.GetFloat("z");
        layer.Opacity = context.GetFloat("opacity", 1);
        layer.DisplayMode = Enum.TryParse<LayerDisplayMode>(context.GetString("displayMode", "Native"), true, out var displayMode)
            ? displayMode
            : LayerDisplayMode.Native;
        layer.Visible = true;

        view.ShowLayer(new LayerRenderRequest(layer.Id, layer.AssetId, layer.Transform.Clone(), layer.Z, layer.DisplayMode, layer.Opacity));
        await PresentationRequests.PlayTransitionAsync(context, view, previousAsset, asset, ct);
    }
}

public sealed class HideLayerHandler : EntryHandler
{
    public override string EntryType => HideLayerEntry.TypeId;
    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var id = context.GetString("handleId");
        if (!context.Runtime.SceneInstances.Remove<Layer>(id, out var removed))
        {
            GameLog.Logger.Warning("Layer hide ignored because handle '{HandleId}' is not active.", id);
            return;
        }

        await PresentationRequests.PlayTransitionAsync(context, view, removed!.AssetId, null, ct);
        view.HideLayer(id);
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
        if (!context.Runtime.SceneInstances.TryGet<AnimatableSceneInstance>(request.HandleId, out var instance))
        {
            GameLog.Logger.Warning("Animate ignored because handle '{HandleId}' is not an active animatable instance.", request.HandleId);
            return;
        }
        var property = instance.AnimatableProperties.FirstOrDefault(item => item.Name == request.Property);
        if (property is null || !property.Accepts(request.To) ||
            (request.From is { } from && !property.Accepts(from)))
        {
            GameLog.Logger.Warning("Animate ignored because '{Property}' does not accept the supplied value.", request.Property);
            return;
        }

        var animation = view.AnimateAsync(request, ct);
        async Task CommitAsync()
        {
            var outcome = await animation;
            if (outcome is AnimationOutcome.Completed or AnimationOutcome.Skipped)
            {
                if (!instance.TrySetAnimationValue(request.Property, request.To, out var error))
                    GameLog.Logger.Warning("Animation completion could not set '{Property}' on '{HandleId}': {Error}", request.Property, request.HandleId, error);
            }
        }

        if (request.Blocking)
        {
            await CommitAsync();
            return;
        }

        _ = CommitAsync().ContinueWith(
            task => GameLog.Logger.Error(task.Exception, "Non-blocking animation failed"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}

public sealed class PlayAnimationPlanHandler : EntryHandler
{
    public override string EntryType => PlayAnimationPlanEntry.TypeId;

    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var plan = context.GetAnimationPlan();
        var events = new TimelineEventDispatcher(context, timeProvider, ct);
        events.Start(plan);
        // Frame-zero events may create a target (for example, the transparent incoming
        // layer of a cross-fade), so validate targets after they have been dispatched.
        if (!ValidateTracks(context.Runtime, plan))
        {
            events.Complete(plan, AnimationOutcome.Replaced);
            return;
        }
        var playback = view.PlayAnimationPlanAsync(plan, ct);

        async Task CompleteAsync()
        {
            var result = await playback;
            events.Complete(plan, result.Outcome);
            CommitStableTracks(context.Runtime, plan, result);
        }

        if (plan.Blocking)
        {
            await CompleteAsync();
            return;
        }

        _ = CompleteAsync().ContinueWith(
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

            var property = instance.AnimatableProperties.FirstOrDefault(candidate => candidate.Name == track.Property);
            if (property is null || track.Keys.Any(key => !property.Accepts(key.Value)))
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
            if (!result.TrackOutcomes.TryGetValue(key, out var outcome) || outcome is not (AnimationOutcome.Completed or AnimationOutcome.Skipped))
                continue;
            if (runtime.SceneInstances.TryGet<AnimatableSceneInstance>(track.HandleId, out var instance) &&
                !instance.TrySetAnimationValue(track.Property, track.Keys[^1].Value, out var error))
                GameLog.Logger.Warning("Animation plan completion could not set '{HandleId}.{Property}': {Error}", track.HandleId, track.Property, error);
        }
    }

    private sealed class TimelineEventDispatcher(EntryContext context, TimeProvider timeProvider, CancellationToken outerCancellation)
    {
        private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        private readonly HashSet<AnimationPlanEventDefinition> _triggered = [];
        public void Start(AnimationPlanDefinition plan)
        {
            foreach (var timelineEvent in plan.Events.Where(item => item.Frame == 0)) Trigger(timelineEvent);
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

        private void Trigger(AnimationPlanEventDefinition timelineEvent)
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
                entry = EntryRegistry.Create(timelineEvent.Type, values: timelineEvent.Parameters.ToDictionary(pair => pair.Key, pair => ToValue(pair.Value)));
            }
            catch (Exception exception)
            {
                GameLog.Logger.Warning(exception, "Animation plan event '{EntryType}' is invalid.", timelineEvent.Type);
                return;
            }

            // Timeline events are detached from the Plan's lifetime. In particular, an end
            // event must still be able to hide an instance after the Plan has been cancelled.
            _ = context.DispatchTimelineEventAsync(entry, CancellationToken.None).ContinueWith(
                task => GameLog.Logger.Error(task.Exception, "Animation plan event '{EntryType}' failed", timelineEvent.Type),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
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
            PresentationRequests.GetOptionalDuration(context),
            context.GetBool("blocking"),
            context.GetString("parameters"));

        if (!string.IsNullOrWhiteSpace(request.InstanceId) && !context.Runtime.SceneState.ActiveEffectIds.Contains(request.InstanceId))
            context.Runtime.SceneState.ActiveEffectIds.Add(request.InstanceId);

        if (request.Duration is { } duration)
        {
            var lifetime = PresentationRequests.CompleteEffectAfterDurationAsync(
                context.Runtime, view, request, duration, timeProvider, ct);
            await PresentationRequests.AwaitIfBlockingAsync(lifetime, request.IsBlocking);
            return;
        }

        await PresentationRequests.AwaitIfBlockingAsync(view.StartEffectAsync(request, ct), request.IsBlocking);
    }
}

public sealed class StopEffectHandler : EntryHandler
{
    public override string EntryType => StopEffectEntry.TypeId;
    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var instanceId = context.GetString("instanceId");
        context.Runtime.SceneState.ActiveEffectIds.Remove(instanceId);
        await view.StopEffectAsync(instanceId, ct);
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

internal static class PresentationRequests
{
    public static async Task PlayTransitionAsync(EntryContext context, IGameView view, string? fromImageId, string? toImageId, CancellationToken ct)
    {
        var id = context.GetString("transitionId");
        if (string.IsNullOrWhiteSpace(id)) return;

        var request = new TransitionRequest(
            id,
            fromImageId,
            toImageId,
            TimeSpan.FromSeconds(Math.Max(0, context.GetFloat("transitionDuration", 0.5f))),
            context.GetBool("transitionBlocking"),
            context.GetString("transitionParameters"));

        context.Runtime.SceneState.ActiveTransition = request.Id;
        await AwaitIfBlockingAsync(view.PlayTransitionAsync(request, ct), request.IsBlocking);
    }

    public static TimeSpan? GetOptionalDuration(EntryContext context)
    {
        var duration = context.GetFloat("duration", -1);
        return duration < 0 ? null : TimeSpan.FromSeconds(duration);
    }

    public static async Task CompleteEffectAfterDurationAsync(
        IGameRuntime runtime,
        IGameView view,
        EffectRequest request,
        TimeSpan duration,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        await view.StartEffectAsync(request, ct);
        await Task.Delay(duration, timeProvider, ct);
        runtime.SceneState.ActiveEffectIds.Remove(request.InstanceId);
        await view.StopEffectAsync(request.InstanceId, ct);
    }

    public static async Task AwaitIfBlockingAsync(Task task, bool isBlocking)
    {
        if (isBlocking)
        {
            await task;
            return;
        }

        _ = task.ContinueWith(
            completed => Serilog.Log.ForContext("LogChannel", "Game").Error(completed.Exception, "Non-blocking presentation operation failed"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
