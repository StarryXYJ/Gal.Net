using GalNet.Core.Entry;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.View;

namespace GalNet.Runtime.Handlers;

public sealed class ShowLayerHandler : EntryHandler
{
    public override string EntryType => ShowLayerEntry.TypeId;
    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var id = context.GetString("id");
        var asset = context.GetString("asset");
        var layer = context.Runtime.SceneState.Layers.Find(candidate => candidate.Id == id);
        var previousAsset = layer?.AssetId;
        if (layer is null)
        {
            layer = new Layer { Id = id };
            context.Runtime.SceneState.Layers.Add(layer);
        }

        layer.AssetId = asset;
        layer.X = context.GetFloat("x");
        layer.Y = context.GetFloat("y");
        layer.Z = context.GetFloat("z");
        layer.Visible = true;

        view.ShowLayer(id, asset, layer.X, layer.Y, layer.Z);
        await PresentationRequests.PlayTransitionAsync(context, view, previousAsset, asset, ct);
    }
}

public sealed class HideLayerHandler : EntryHandler
{
    public override string EntryType => HideLayerEntry.TypeId;
    public override async Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var id = context.GetString("id");
        var previousAsset = context.Runtime.SceneState.Layers.Find(layer => layer.Id == id)?.AssetId;
        context.Runtime.SceneState.Layers.RemoveAll(layer => layer.Id == id);

        await PresentationRequests.PlayTransitionAsync(context, view, previousAsset, null, ct);
        view.HideLayer(id);
    }
}

public sealed class MoveLayerHandler : EntryHandler
{
    public override string EntryType => MoveLayerEntry.TypeId;
    public override Task ExecuteAsync(EntryContext context, IGameView view, TimeProvider timeProvider, CancellationToken ct)
    {
        var id = context.GetString("id");
        var layer = context.Runtime.SceneState.Layers.Find(candidate => candidate.Id == id);
        if (layer is not null)
        {
            layer.X = context.GetFloat("x");
            layer.Y = context.GetFloat("y");
            layer.Z = context.GetFloat("z");
        }

        view.MoveLayer(id, context.GetFloat("x"), context.GetFloat("y"), context.GetFloat("z"), context.GetFloat("duration", 0.5f));
        return Task.CompletedTask;
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
