using GalNet.Core.Entry;
using GalNet.Core.Handler;
using System.Text.Json;

namespace GalNet.Runtime.Handlers;

public sealed class ShowLayerHandler : EntryHandler
{
    public override string EntryType => ShowLayerEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx)
    {
        var transition = ctx.GetString("transition");
        if (transition.Length > 0) ctx.View.ApplyTransition(transition, ctx.GetFloat("duration", 0.5f));
        ctx.View.ShowLayer(ctx.GetString("id"), ctx.GetString("asset"), ctx.GetFloat("x"), ctx.GetFloat("y"), ctx.GetFloat("z"));
    }
}

public sealed class HideLayerHandler : EntryHandler
{
    public override string EntryType => HideLayerEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx)
    {
        var transition = ctx.GetString("transition");
        if (transition.Length > 0) ctx.View.ApplyTransition(transition, ctx.GetFloat("duration", 0.5f));
        ctx.View.HideLayer(ctx.GetString("id"));
    }
}

public sealed class MoveLayerHandler : EntryHandler
{
    public override string EntryType => MoveLayerEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.MoveLayer(ctx.GetString("id"), ctx.GetFloat("x"), ctx.GetFloat("y"), ctx.GetFloat("z"), ctx.GetFloat("duration", 0.5f));
}

public sealed class PlayAudioHandler : EntryHandler
{
    public override string EntryType => PlayAudioEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.PlayAudio(ctx.GetString("channel", "bgm"), ctx.GetString("asset"), ctx.GetFloat("volume", 0.8f), ctx.GetString("mode", "once"), ctx.GetInt("times", 1));
}

public sealed class StopAudioHandler : EntryHandler
{
    public override string EntryType => StopAudioEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.StopAudio(ctx.GetString("channel", "bgm"));
}

public sealed class PauseAudioHandler : EntryHandler
{
    public override string EntryType => PauseAudioEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.PauseAudio(ctx.GetString("channel", "bgm"));
}

public sealed class ResumeAudioHandler : EntryHandler
{
    public override string EntryType => ResumeAudioEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.ResumeAudio(ctx.GetString("channel", "bgm"));
}

public sealed class EnqueueAudioHandler : EntryHandler
{
    public override string EntryType => EnqueueAudioEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.EnqueueAudio(ctx.GetString("channel", "bgm"), ctx.GetString("asset"), ctx.GetInt("times", 1));
}

public sealed class PlayVideoHandler : EntryHandler
{
    public override string EntryType => PlayVideoEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.PlayVideo(ctx.GetString("asset"));
}

public sealed class StopVideoHandler : EntryHandler
{
    public override string EntryType => StopVideoEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.StopVideo();
}

public sealed class ShowDialogueHandler : EntryHandler
{
    public override string EntryType => ShowDialogueEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.ShowDialogue();
}

public sealed class HideDialogueHandler : EntryHandler
{
    public override string EntryType => HideDialogueEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.HideDialogue();
}

public sealed class ApplyEffectHandler : EntryHandler
{
    public override string EntryType => ApplyEffectEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx)
    {
        Dictionary<string, object> parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(ctx.GetString("parameters")) ?? [];
        }
        catch (JsonException)
        {
            parameters = [];
        }
        ctx.View.ApplyEffect(ctx.GetString("type"), parameters);
    }
}

public sealed class StopEffectHandler : EntryHandler
{
    public override string EntryType => StopEffectEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx) => ctx.View.StopEffect(ctx.GetString("id"));
}

public sealed class SetVariableHandler : EntryHandler
{
    public override string EntryType => SetVariableEntry.TypeId;
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx)
    {
        var target = ctx.GetString("target");
        var expression = ctx.GetString("expression");
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(expression))
        {
            Serilog.Log.ForContext("LogChannel", "Game").Warning("Set variable skipped because target or expression is empty: target={Target}", target);
            return;
        }

        try
        {
            var result = ctx.Runtime.EvaluateExpression(expression);
            if (result is null)
            {
                Serilog.Log.ForContext("LogChannel", "Game").Warning("Set variable expression returned null: target={Target}, expression={Expression}", target, expression);
                return;
            }
            ctx.Runtime.SetVariable(target, result);
        }
        catch (Exception exception)
        {
            Serilog.Log.ForContext("LogChannel", "Game").Warning(exception, "Set variable expression failed: target={Target}, expression={Expression}", target, expression);
        }
    }
}
