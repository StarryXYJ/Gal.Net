using GalNet.Contracts.Builtins;

namespace GalNet.Editor.Shared.Services;

public enum SceneIdentityDomain { Object, Playback, AudioEffect }
public sealed record SceneOutputSlot(string Field, string Role, SceneIdentityDomain Domain, SceneTargetKind Kind, string? Container = null);
public sealed record SceneReferenceSlot(string Field, SceneIdentityDomain Domain, IReadOnlyList<SceneTargetKind> Kinds, bool RequiresVariants = false, bool AllowsGlobal = false, string? Container = null);
public sealed record SceneEntrySemantics(IReadOnlyList<SceneOutputSlot> Outputs, IReadOnlyList<SceneReferenceSlot> References, bool SceneNeutral = false, bool Timeline = false);

/// <summary>Built-in author relationships, independent of value schemas and runtime execution.</summary>
public static class SceneAuthoringSemantics
{
    private static readonly SceneEntrySemantics Neutral = new([], [], SceneNeutral: true);
    private static readonly IReadOnlyDictionary<string, SceneEntrySemantics> Entries = Create();
    public static SceneEntrySemantics? Find(string type) => Entries.GetValueOrDefault(type);
    public static SceneReferenceSlot? Reference(string type, string field) => Find(type)?.References.FirstOrDefault(slot => slot.Field == field);

    private static IReadOnlyDictionary<string, SceneEntrySemantics> Create()
    {
        var entries = new Dictionary<string, SceneEntrySemantics>(StringComparer.Ordinal);
        SceneOutputSlot Output(string field, string role, SceneTargetKind kind) => new(field, role, kind == SceneTargetKind.Playback ? SceneIdentityDomain.Playback : SceneIdentityDomain.Object, kind);
        SceneReferenceSlot Reference(string field, params SceneTargetKind[] kinds) => new(field, kinds.Contains(SceneTargetKind.Playback) ? SceneIdentityDomain.Playback : SceneIdentityDomain.Object, Array.AsReadOnly(kinds));
        void Add(string type, SceneOutputSlot[] outputs, SceneReferenceSlot[] references, bool timeline = false) => entries.Add(type, new(Array.AsReadOnly(outputs), Array.AsReadOnly(references), Timeline: timeline));
        foreach (var type in new[] { BuiltinEntryIds.ShowLayer, BuiltinEntryIds.ShowColorLayer, BuiltinEntryIds.PlayVideo, BuiltinEntryIds.ShowVariants })
            Add(type, [Output("handleId", "primaryLayer", SceneTargetKind.Layer)], []);
        Add(BuiltinEntryIds.PlayParticleEmitter, [Output("instanceId", "particle", SceneTargetKind.Particle)], []);
        Add(BuiltinEntryIds.ApplyEffect, [Output("instanceId", "effect", SceneTargetKind.Effect)], [Reference("targetHandleId", SceneTargetKind.Layer) with { AllowsGlobal = true }]);
        foreach (var type in new[] { BuiltinEntryIds.HideLayer, BuiltinEntryIds.MoveLayer, BuiltinEntryIds.ReplaceLayer, BuiltinEntryIds.StopVideo })
            Add(type, [], [Reference("handleId", SceneTargetKind.Layer)]);
        Add(BuiltinEntryIds.SelectVariants, [], [Reference("handleId", SceneTargetKind.Layer) with { RequiresVariants = true }]);
        Add(BuiltinEntryIds.StopParticleEmitter, [], [Reference("instanceId", SceneTargetKind.Particle)]);
        Add(BuiltinEntryIds.StopEffect, [], [Reference("instanceId", SceneTargetKind.Effect)]);
        Add(BuiltinEntryIds.Animate, [Output("playbackHandleId", "playback", SceneTargetKind.Playback)], [Reference("handleId", SceneTargetKind.Layer, SceneTargetKind.Particle, SceneTargetKind.Effect)]);
        Add(BuiltinEntryIds.StopAnimation, [], [Reference("playbackHandleId", SceneTargetKind.Playback)]);
        Add(BuiltinEntryIds.MotionPreset, [Output("playbackHandleId", "playback", SceneTargetKind.Playback)], [Reference("handleId", SceneTargetKind.Layer)]);
        Add(BuiltinEntryIds.ShakePreset, [Output("playbackHandleId", "playback", SceneTargetKind.Playback)], [Reference("handleId", SceneTargetKind.Layer) with { Container = "targets" }]);
        Add(BuiltinEntryIds.ImpactPreset, [Output("playbackHandleId", "playback", SceneTargetKind.Playback)], [Reference("handleId", SceneTargetKind.Layer) with { Container = "targets" }]);
        Add(BuiltinEntryIds.SpeedLinesPreset, [Output("playbackHandleId", "playback", SceneTargetKind.Playback)], []);
        foreach (var type in new[] { BuiltinEntryIds.CrossFadeTransition, BuiltinEntryIds.BlindsTransition, BuiltinEntryIds.SlideTransition, BuiltinEntryIds.BlackFadeTransition, BuiltinEntryIds.WhiteFadeTransition, BuiltinEntryIds.ColorFadeTransition, BuiltinEntryIds.RevealTransition, BuiltinEntryIds.BlurTransition })
        {
            var cross = type is BuiltinEntryIds.CrossFadeTransition or BuiltinEntryIds.BlindsTransition or BuiltinEntryIds.RevealTransition or BuiltinEntryIds.BlurTransition;
            Add(type, [Output(cross ? "newHandleId" : "toLayerHandleId", "primaryLayer", SceneTargetKind.Layer), Output("playbackHandleId", "playback", SceneTargetKind.Playback)], [Reference(cross ? "oldHandleId" : "fromLayerHandleId", SceneTargetKind.Layer)]);
        }
        Add(BuiltinEntryIds.PlayTimeline, [Output("playbackHandleId", "playback", SceneTargetKind.Playback) with { Container = "timeline" }], [], timeline: true);
        entries.Add(BuiltinEntryIds.ApplyAudioSEffect, new([new("instanceId", "audioEffect", SceneIdentityDomain.AudioEffect, SceneTargetKind.AudioEffect)], [], SceneNeutral: true));
        foreach (var type in new[] { BuiltinEntryIds.StopAudioSEffect, BuiltinEntryIds.UpdateAudioSEffect })
            entries.Add(type, new([], [new("instanceId", SceneIdentityDomain.AudioEffect, [SceneTargetKind.AudioEffect])], SceneNeutral: true));
        foreach (var type in new[] { BuiltinEntryIds.Text, BuiltinEntryIds.ShowDialogue, BuiltinEntryIds.HideDialogue, BuiltinEntryIds.ShowNvl, BuiltinEntryIds.HideNvl, BuiltinEntryIds.ClearNvl, BuiltinEntryIds.AppendNvl, BuiltinEntryIds.TextVoice, BuiltinEntryIds.AppendVoiceNvl, BuiltinEntryIds.Wait, BuiltinEntryIds.SetVariable, BuiltinEntryIds.UnlockGallery, BuiltinEntryIds.PlayAudio, BuiltinEntryIds.StopAudio, BuiltinEntryIds.PauseAudio, BuiltinEntryIds.ResumeAudio, BuiltinEntryIds.NextAudio, BuiltinEntryIds.ClearAudio, BuiltinEntryIds.EnqueueAudio, BuiltinEntryIds.SetAudioLevel, BuiltinEntryIds.BurstParticles }) entries.Add(type, Neutral);
        return entries;
    }
}
