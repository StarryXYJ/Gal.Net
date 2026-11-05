using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using System.Text.Json;

namespace GalNet.Contracts.Builtins;

internal static partial class VisualPresetCompiler
{
    private static void OwnEvents(TimelineDefinition timeline)
    {
        foreach (var item in timeline.Events)
            item.Execution = item.Frame == 0 ? TimelineEventExecution.Once : TimelineEventExecution.OnCompletion;
    }

    public static IReadOnlyList<PrimitiveEntry> Mask(EntryCompileContext context, MaskPresetOptions options)
    {
        var clip = Clip(options); var layer = Require(options.InstanceId, "instanceId"); var effect = layer + "/mask-effect";
        if (options.Parameters.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Mask parameters must be an object.");
        var parameters = options.Parameters.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        if ((options.Motion is MaskMotion.Show or MaskMotion.Hide) && parameters.TryGetValue("mode", out var shape) && shape.ValueKind == JsonValueKind.String && shape.GetString() == "Image" &&
            (!parameters.TryGetValue("mask", out var image) || image.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(image.GetString())))
            throw new InvalidDataException("Image mask requires an explicit mask texture.");
        parameters["overlay"] = Json(true);
        if (options.Motion is MaskMotion.LetterboxIn or MaskMotion.LetterboxOut)
        { parameters["mode"] = Json("Letterbox"); if (!parameters.ContainsKey("height")) parameters["height"] = Json(.24f); }
        else if (options.Motion is MaskMotion.Blink or MaskMotion.Open or MaskMotion.Close)
        {
            parameters["mode"] = Json("Eyelid");
            if (!parameters.ContainsKey("feather")) parameters["feather"] = Json(.025f);
        }
        var from = options.Motion is MaskMotion.Blink or MaskMotion.Close or MaskMotion.LetterboxOut ? 1f : 0f;
        var to = options.Motion is MaskMotion.Close or MaskMotion.LetterboxOut or MaskMotion.Hide ? 0f : 1f;
        if (options.Motion is MaskMotion.Show or MaskMotion.Hide)
        { if (!parameters.ContainsKey("progress")) parameters["progress"] = Json(1f); }
        else parameters["progress"] = Json(from);
        clip.Events.Add(Event(0, BuiltinEntryIds.ShowColorLayer, new { handleId = layer, color = "#00000000", opacity = options.Motion == MaskMotion.Show ? 0 : 1, z = options.Z, overlay = options.Overlay }));
        clip.Events.Add(Apply(0, effect, layer, options.Program, parameters));
        var track = options.Motion is MaskMotion.Show or MaskMotion.Hide
            ? Track(layer, "opacity", clip.DurationFrames, options.Motion == MaskMotion.Show ? 0 : 1, options.Motion == MaskMotion.Show ? 1 : 0)
            : Track(effect, "progress", clip.DurationFrames, from, to);
        if (options.Motion == MaskMotion.Blink)
        {
            if (clip.DurationFrames < 4) throw new InvalidDataException("Blink requires at least four frames.");
            var close = Math.Clamp((int)(clip.DurationFrames * (1 - options.HoldRatio) / 2), 1, clip.DurationFrames - 2);
            var open = Math.Max(close + 1, clip.DurationFrames - close);
            track.Keys.Insert(1, new() { Frame = close, Value = 0 });
            track.Keys.Insert(2, new() { Frame = open, Value = 0 });
        }
        foreach (var key in track.Keys) key.InterpolationToNext = AnimationInterpolation.CubicHermite;
        clip.Tracks.Add(track);
        if (options.Motion is MaskMotion.Blink or MaskMotion.Open or MaskMotion.LetterboxOut or MaskMotion.Hide)
        { clip.Events.Add(Stop(clip.DurationFrames, effect)); clip.Events.Add(CrossFadeTransition.HideLayer(layer, clip.DurationFrames)); }
        return Finish(context, clip, options.BatchId);
    }

    public static IReadOnlyList<PrimitiveEntry> Flash(EntryCompileContext context, FlashPresetOptions options)
    {
        var clip = Clip(options);
        if (!Layer.IsValidColor(options.Color) || options.DurationFrames < options.Count * 2) throw new InvalidDataException("Flash requires a color and at least two frames per flash.");
        var layer = Id(clip, "flash");
        clip.Events.Add(Event(0, BuiltinEntryIds.ShowColorLayer, new { handleId = layer, color = options.Color, opacity = options.Strength, overlay = options.Overlay, z = options.Z }));
        var track = new TimelineTrackDefinition { HandleId = layer, Property = "opacity" };
        for (var i = 0; i < options.Count; i++)
        {
            track.Keys.Add(new() { Frame = i * clip.DurationFrames / options.Count, Value = options.Strength });
            track.Keys.Add(new() { Frame = (i + 1) * clip.DurationFrames / options.Count - 1, Value = 0 });
        }
        track.Keys.Add(new() { Frame = clip.DurationFrames, Value = 0 }); clip.Tracks.Add(track);
        clip.Events.Add(CrossFadeTransition.HideLayer(layer, clip.DurationFrames));
        return Finish(context, clip, options.BatchId);
    }

    public static IReadOnlyList<PrimitiveEntry> Idle(EntryCompileContext context, IdlePresetOptions options)
    {
        var clip = Clip(options); Require(options.HandleId, "handleId"); Sustain(clip, options.PlaybackMode);
        if (options.Motion == IdleMotion.Breathe)
        {
            clip.Tracks.Add(Pulse(options.HandleId, "transform.scaleY", clip.DurationFrames, options.ScaleAmount));
            clip.Tracks.Add(Pulse(options.HandleId, "transform.y", clip.DurationFrames, -options.Distance));
        }
        else
        {
            var sway = Track(options.HandleId, "transform.rotationDegrees", clip.DurationFrames, 0, 0, AnimationBlendMode.Additive);
            if (clip.DurationFrames >= 4)
            {
                sway.Keys.Insert(1, new() { Frame = clip.DurationFrames / 4, Value = options.RotationDegrees });
                sway.Keys.Insert(2, new() { Frame = clip.DurationFrames / 2, Value = 0 });
                sway.Keys.Insert(3, new() { Frame = clip.DurationFrames * 3 / 4, Value = -options.RotationDegrees });
            }
            clip.Tracks.Add(sway);
        }
        return Finish(context, clip, options.BatchId);
    }

    public static IReadOnlyList<PrimitiveEntry> Mood(EntryCompileContext context, MoodPresetOptions options)
    {
        var clip = Clip(options); var id = Require(options.InstanceId, "instanceId");
        clip.Events.Add(Apply(0, id, options.TargetHandleId, options.Program, new { mode = options.Mood.ToString(), strength = options.Exit ? options.Strength : 0 }));
        clip.Tracks.Add(Track(id, "strength", clip.DurationFrames, options.Exit ? options.Strength : 0, options.Exit ? 0 : options.Strength));
        if (options.Exit) clip.Events.Add(Stop(clip.DurationFrames, id));
        return Finish(context, clip, options.BatchId);
    }

    public static IReadOnlyList<PrimitiveEntry> Heartbeat(EntryCompileContext context, HeartbeatPresetOptions options)
    {
        var clip = Clip(options); Sustain(clip, options.PlaybackMode);
        if (clip.DurationFrames < 8) throw new InvalidDataException("Heartbeat requires at least eight frames.");
        var effect = Id(clip, "heartbeat");
        clip.Events.Add(Apply(0, effect, "", options.Program, new { strength = 0, radius = .25, softness = .5 }));
        var track = Track(effect, "strength", clip.DurationFrames, 0, 0);
        foreach (var (part, amount) in new[] { (.12f, 1f), (.25f, .15f), (.37f, .65f), (.65f, 0f) })
            track.Keys.Insert(track.Keys.Count - 1, new() { Frame = (int)MathF.Round(part * clip.DurationFrames), Value = amount * options.Strength });
        clip.Tracks.Add(track); clip.Events.Add(Stop(clip.DurationFrames, effect));
        if (!string.IsNullOrWhiteSpace(options.HandleId))
            foreach (var property in new[] { "transform.scaleX", "transform.scaleY" })
            {
                var pulse = Track(options.HandleId, property, clip.DurationFrames, 0, 0, AnimationBlendMode.Additive);
                pulse.Keys = track.Keys.Select(key => new TimelineKeyframeDefinition { Frame = key.Frame, Value = key.Value * .025f }).ToList(); clip.Tracks.Add(pulse);
            }
        if (options.PlaybackMode == VisualPlaybackMode.UntilStopped) OwnEvents(clip);
        if (!string.IsNullOrWhiteSpace(options.Sound)) clip.Events.Add(Event(0, BuiltinEntryIds.PlayAudio, new { trackId = options.TrackId, asset = options.Sound, placement = "replace", gain = options.Strength }));
        return Finish(context, clip, options.BatchId);
    }

    public static IReadOnlyList<PrimitiveEntry> Parallax(EntryCompileContext context, ParallaxPresetOptions options)
    {
        var clip = Clip(options); var seen = new HashSet<string>();
        if (options.Targets.ValueKind != JsonValueKind.Array || options.Targets.GetArrayLength() == 0) throw new InvalidDataException("Parallax requires explicit target baselines.");
        foreach (var item in options.Targets.EnumerateArray())
        {
            var handle = Require(item.GetProperty("handleId").GetString(), "handleId");
            if (!seen.Add(handle)) throw new InvalidDataException("Duplicate parallax target.");
            var transform = TransitionEntrySupport.Transform(item.GetProperty("transform"), "transform", "parallax");
            var factor = item.TryGetProperty("factor", out var f) ? f.GetSingle() : 1;
            if (!float.IsFinite(factor)) throw new InvalidDataException("Parallax factor must be finite.");
            var scale = 1 + (options.Scale - 1) * factor;
            if (scale <= 0) throw new InvalidDataException("Parallax scale must remain positive.");
            clip.Tracks.Add(Track(handle, "transform.x", clip.DurationFrames, transform.X, transform.X + options.DistanceX * factor + (transform.X - options.AnchorX) * (scale - 1)));
            clip.Tracks.Add(Track(handle, "transform.y", clip.DurationFrames, transform.Y, transform.Y + options.DistanceY * factor + (transform.Y - options.AnchorY) * (scale - 1)));
            clip.Tracks.Add(Track(handle, "transform.scaleX", clip.DurationFrames, transform.ScaleX, transform.ScaleX * scale));
            clip.Tracks.Add(Track(handle, "transform.scaleY", clip.DurationFrames, transform.ScaleY, transform.ScaleY * scale));
        }
        return Finish(context, clip, options.BatchId);
    }

    public static IReadOnlyList<PrimitiveEntry> Emphasis(EntryCompileContext context, EmphasisPresetOptions options, bool dialogue)
    {
        var clip = Clip(options); var identity = Require(options.InstanceId, "instanceId");
        if (dialogue) clip.Blocking = false;
        if (options.HandleIds.ValueKind != JsonValueKind.Array || options.HandleIds.GetArrayLength() == 0) throw new InvalidDataException("Emphasis requires explicit participants.");
        var handles = options.HandleIds.EnumerateArray().Select(value => Require(value.GetString(), "handleIds")).ToArray();
        if (handles.Distinct(StringComparer.Ordinal).Count() != handles.Length || !options.Clear && !handles.Contains(options.ActiveHandleId)) throw new InvalidDataException("Active emphasis target must be one of the unique participants.");
        foreach (var handle in handles)
        {
            var effect = identity + "/" + handle;
            if (!options.Clear)
                clip.Events.Add(Event(0, BuiltinEntryIds.ApplyEffect, new { instanceId = effect, targetHandleId = handle, program = Require(options.Program, "program"), ensureOnly = true, parameters = new { exposure = 0 } }));
            var track = Track(effect, "exposure", clip.DurationFrames, 0, options.Clear || handle == options.ActiveHandleId ? 0 : options.DimExposure);
            track.StartFromCurrent = true; clip.Tracks.Add(track);
            if (options.Clear) clip.Events.Add(Stop(clip.DurationFrames, effect));
        }
        var output = Finish(context, clip, options.BatchId).ToList();
        if (dialogue)
        {
            if (!string.IsNullOrWhiteSpace(options.Voice)) output.Add(new(BuiltinEntryIds.PlayAudio, ValueCodec.SerializeFields(new AudioPlayOptions { TrackId = options.TrackId, Asset = options.Voice, Placement = Audio.AudioPlacement.ReplaceIfDifferent, AutoBlocking = true })) { Condition = context.Condition });
            output.Add(new(BuiltinEntryIds.Text, ValueCodec.SerializeFields(new DialogueTextOptions { Speaker = options.Speaker, Content = options.Content })) { Condition = context.Condition });
        }
        return output;
    }

    public static IReadOnlyList<PrimitiveEntry> StylizedTransition(EntryCompileContext context, StylizedTransitionOptions options, string kind)
    {
        var clip = Transition(options); var frames = clip.DurationFrames;
        clip.Events[0].Parameters["opacity"] = Json(0f);
        clip.Tracks.Add(Track(options.NewHandleId!, "opacity", frames, 0, 1));
        if (kind == "flash")
        {
            var layer = Id(clip, "flash"); clip.Events.Add(Event(0, BuiltinEntryIds.ShowColorLayer, new { handleId = layer, color = "#FFFFFF", opacity = 0, z = options.Z + 1000 }));
            var flash = Track(layer, "opacity", frames, 0, 0); flash.Keys.Insert(1, new() { Frame = frames / 2, Value = Math.Min(1, options.Strength) }); clip.Tracks.Add(flash);
            clip.Events.Add(CrossFadeTransition.HideLayer(layer, frames));
        }
        else if (kind == "zoom")
        {
            var transform = TransitionEntrySupport.Transform(options.Transform, "transform", "zoom"); var scale = 1 + options.Strength * .2f;
            clip.Tracks.Add(Track(options.NewHandleId!, "transform.scaleX", frames, transform.ScaleX / scale, transform.ScaleX));
            clip.Tracks.Add(Track(options.NewHandleId!, "transform.scaleY", frames, transform.ScaleY / scale, transform.ScaleY));
            clip.Tracks.Add(Track(options.NewHandleId!, "transform.x", frames, options.AnchorX + (transform.X - options.AnchorX) / scale, transform.X));
            clip.Tracks.Add(Track(options.NewHandleId!, "transform.y", frames, options.AnchorY + (transform.Y - options.AnchorY) / scale, transform.Y));
        }
        else foreach (var incoming in new[] { false, true })
        {
            var handle = incoming ? options.NewHandleId! : options.OldHandleId!; var effect = Id(clip, incoming ? "incoming" : "outgoing");
            var property = kind == "pixelate" ? "blockSize" : "strength"; var neutral = kind == "pixelate" ? 1 : 0; var peak = kind == "pixelate" ? Math.Clamp(options.Strength * 48, 1, 256) : Math.Min(1, options.Strength);
            clip.Events.Add(Apply(0, effect, handle, Require(options.Program ?? BuiltinVisualResources.Programs.GetValueOrDefault(kind), "program"), new Dictionary<string, float> { [property] = incoming ? peak : neutral }));
            clip.Tracks.Add(Track(effect, property, frames, incoming ? peak : neutral, incoming ? neutral : peak));
            if (kind == "glitch") clip.Tracks.Add(Track(effect, "time", frames, 0, (float)frames / clip.FrameRate));
            clip.Events.Add(Stop(frames, effect));
        }
        clip.Events.Add(CrossFadeTransition.HideLayer(options.OldHandleId!, frames));
        return Finish(context, clip, options.BatchId);
    }
}
