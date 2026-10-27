using System.Text.Json;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;

namespace GalNet.Contracts.Builtins;

/// <summary>Pure composition. Shader locators are always supplied by the author entry.</summary>
internal static class VisualPresetCompiler
{
    private static JsonElement Json<T>(T value) => JsonCodec.SerializeToElement(value, TransitionEntrySupport.TimelineJsonOptions);
    private static string Require(string? value, string name) => TransitionEntrySupport.Require(value, name, "visual preset");
    private static TimelineEventDefinition Event(int frame, string type, object parameters) => new()
    {
        Frame = frame,
        Type = type,
        Parameters = Json(parameters).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal)
    };
    private static TimelineTrackDefinition Track(string handle, string property, int frames, float from, float to, AnimationBlendMode blend = AnimationBlendMode.Replace) => new()
    {
        HandleId = handle,
        Property = property,
        BlendMode = blend,
        Keys = [new() { Frame = 0, Value = from }, new() { Frame = frames, Value = to }]
    };
    private static TimelineDefinition Clip(VisualClipOptions options)
    {
        Require(options.PlaybackHandleId, "playbackHandleId");
        if (options.DurationFrames is < 2 or > 36000 || options.FrameRate is < 1 or > 240) throw new InvalidDataException("Invalid visual clip duration or frame rate.");
        return new() { PlaybackHandleId = options.PlaybackHandleId, DurationFrames = options.DurationFrames, FrameRate = options.FrameRate, Blocking = options.Blocking, Skippable = options.Skippable };
    }
    private static TimelineDefinition Transition(CrossFadeOptionsBase options)
    {
        Require(options.PlaybackHandleId, "playbackHandleId");
        Require(options.OldHandleId, "oldHandleId");
        Require(options.NewHandleId, "newHandleId");
        Require(options.AssetId, "assetId");
        if (options.OldHandleId == options.NewHandleId) throw new InvalidDataException("Outgoing and incoming layer handles must differ.");
        if (options.OldHandleId!.StartsWith(options.PlaybackHandleId + "/visual/", StringComparison.Ordinal) || options.NewHandleId!.StartsWith(options.PlaybackHandleId + "/visual/", StringComparison.Ordinal))
            throw new InvalidDataException("Layer handles cannot use the playback's reserved /visual/ namespace.");
        if (options.DurationFrames is < 2 or > 36000 || options.FrameRate is < 1 or > 240 || !float.IsFinite(options.Z)) throw new InvalidDataException("Invalid transition duration, frame rate or Z.");
        _ = TransitionEntrySupport.Transform(options.Transform, "transform", "visual transition");
        var timeline = new TimelineDefinition { PlaybackHandleId = options.PlaybackHandleId!, DurationFrames = options.DurationFrames, FrameRate = options.FrameRate, Blocking = true, Skippable = true };
        timeline.Events.Add(CrossFadeTransition.ShowLayer(options.NewHandleId!, options.AssetId!, options.Transform, options.Z, 1, TransitionEntrySupport.DisplayMode(options.DisplayMode)));
        return timeline;
    }
    private static IReadOnlyList<PrimitiveEntry> Finish(EntryCompileContext context, TimelineDefinition timeline, string? batch)
    {
        TimelineEventCapabilities.ValidateStructure(timeline);
        foreach (var item in timeline.Events) TimelineEventCapabilities.NormalizeEvent(item);
        return [TransitionEntrySupport.TimelineEntry(context, timeline, TransitionEntrySupport.NullIfWhiteSpace(batch))];
    }
    private static string Id(TimelineDefinition timeline, string suffix) => timeline.PlaybackHandleId + "/visual/" + suffix;
    private static TimelineEventDefinition Apply(int frame, string id, string handle, string program, object parameters, int order = 0) =>
        Event(frame, BuiltinEntryIds.ApplyEffect, new { program = Require(program, "program"), instanceId = id, targetHandleId = handle, order, parameters });
    private static TimelineEventDefinition Stop(int frame, string id) => Event(frame, BuiltinEntryIds.StopEffect, new { instanceId = id });

    public static IReadOnlyList<PrimitiveEntry> Reveal(EntryCompileContext context, RevealTransitionOptions options)
    {
        var timeline = Transition(options);
        var id = Id(timeline, "reveal");
        object parameters;
        if (options.Mode == RevealMode.Mask)
            parameters = new { progress = 0, softness = options.Softness, mask = Require(options.MaskAsset, "maskAsset") };
        else
        {
            if (options.Center.ValueKind != JsonValueKind.Array || options.Center.GetArrayLength() != 2 ||
                options.Center.EnumerateArray().Any(c => !c.TryGetSingle(out var f) || !float.IsFinite(f) || f < 0 || f > 1))
                throw new InvalidDataException("Reveal center requires two normalized coordinates.");
            parameters = new { progress = 0, softness = options.Softness, mode = options.Mode.ToString(), angle = options.Angle, count = options.Count, center = options.Center };
        }
        timeline.Events.Add(Apply(0, id, options.NewHandleId!, options.Program, parameters));
        timeline.Tracks.Add(Track(id, "progress", timeline.DurationFrames, 0, 1));
        timeline.Events.Add(Stop(timeline.DurationFrames, id));
        timeline.Events.Add(CrossFadeTransition.HideLayer(options.OldHandleId!, timeline.DurationFrames));
        return Finish(context, timeline, options.BatchId);
    }

    public static IReadOnlyList<PrimitiveEntry> Blur(EntryCompileContext context, BlurTransitionOptions options)
    {
        var timeline = Transition(options);
        timeline.Tracks.Add(Track(options.OldHandleId!, "opacity", timeline.DurationFrames, 1, 0));
        timeline.Tracks.Add(Track(options.NewHandleId!, "opacity", timeline.DurationFrames, 0, 1));
        // The incoming layer must already be transparent when frame-zero events dispatch.
        timeline.Events[0].Parameters["opacity"] = Json(0f);
        foreach (var incoming in new[] { false, true })
            for (var axis = 0; axis < 2; axis++)
            {
                var id = Id(timeline, (incoming ? "new" : "old") + "-blur-" + axis);
                var handle = incoming ? options.NewHandleId! : options.OldHandleId!;
                timeline.Events.Add(Apply(0, id, handle, options.BlurProgram, new { radius = incoming ? options.Radius : 0, direction = axis == 0 ? new[] { 1, 0 } : new[] { 0, 1 } }, axis));
                timeline.Tracks.Add(Track(id, "radius", timeline.DurationFrames, incoming ? options.Radius : 0, incoming ? 0 : options.Radius));
                timeline.Events.Add(Stop(timeline.DurationFrames, id));
            }
        timeline.Events.Add(CrossFadeTransition.HideLayer(options.OldHandleId!, timeline.DurationFrames));
        return Finish(context, timeline, options.BatchId);
    }

    public static IReadOnlyList<PrimitiveEntry> Motion(EntryCompileContext context, MotionPresetOptions options)
    {
        var timeline = Clip(options);
        var handle = Require(options.HandleId, "handleId");
        var frames = timeline.DurationFrames;
        var transform = TransitionEntrySupport.Transform(options.Transform, "transform", "motion");
        var angle = options.Angle * MathF.PI / 180f;
        var x = MathF.Cos(angle) * options.Distance;
        var y = MathF.Sin(angle) * options.Distance;
        void Add(string property, float from, float to) => timeline.Tracks.Add(Track(handle, property, frames, from, to));
        switch (options.Motion)
        {
            case MotionPreset.FadeIn: Add("opacity", 0, 1); break;
            case MotionPreset.FadeOut: Add("opacity", 1, 0); break;
            case MotionPreset.SlideIn:
                Add("opacity", 0, 1);
                Add("transform.x", transform.X - x, transform.X);
                Add("transform.y", transform.Y - y, transform.Y);
                break;
            case MotionPreset.SlideOut:
                Add("opacity", 1, 0);
                Add("transform.x", transform.X, transform.X + x);
                Add("transform.y", transform.Y, transform.Y + y);
                break;
            case MotionPreset.PopIn:
                Add("opacity", 0, 1);
                foreach (var (property, value) in new[] { ("transform.scaleX", transform.ScaleX), ("transform.scaleY", transform.ScaleY) })
                {
                    var track = Track(handle, property, frames, value * .8f, value);
                    track.Keys.Insert(1, new() { Frame = frames / 2, Value = value * 1.08f });
                    timeline.Tracks.Add(track);
                }
                break;
            case MotionPreset.Jump: timeline.Tracks.Add(Pulse(handle, "transform.y", frames, -options.Distance)); break;
            case MotionPreset.Nod: timeline.Tracks.Add(Pulse(handle, "transform.rotationDegrees", frames, options.RotationDegrees)); break;
            case MotionPreset.ShakeHead:
                AddShake(timeline, handle, options.Distance, 2, 0); break;
            case MotionPreset.Push:
                Add("transform.x", transform.X, transform.X + x + (transform.X - options.AnchorX) * (options.Scale - 1));
                Add("transform.y", transform.Y, transform.Y + y + (transform.Y - options.AnchorY) * (options.Scale - 1));
                Add("transform.scaleX", transform.ScaleX, transform.ScaleX * options.Scale);
                Add("transform.scaleY", transform.ScaleY, transform.ScaleY * options.Scale);
                break;
            default: throw new InvalidDataException("Unknown motion preset.");
        }
        return Finish(context, timeline, options.BatchId);
    }
    private static TimelineTrackDefinition Pulse(string handle, string property, int frames, float peak)
    {
        var track = Track(handle, property, frames, 0, 0, AnimationBlendMode.Additive);
        track.Keys.Insert(1, new() { Frame = frames / 2, Value = peak });
        return track;
    }
    private static void AddShake(TimelineDefinition timeline, string handle, float amplitude, int cycles, float angle, bool sustained = false)
    {
        var samples = Math.Min(timeline.DurationFrames, cycles * 4);
        var radians = angle * MathF.PI / 180;
        foreach (var (property, axis) in new[] { ("transform.x", MathF.Cos(radians)), ("transform.y", MathF.Sin(radians)) })
        {
            var track = new TimelineTrackDefinition { HandleId = handle, Property = property, BlendMode = AnimationBlendMode.Additive };
            for (var i = 0; i <= samples; i++)
            {
                var t = (float)i / samples;
                track.Keys.Add(new() { Frame = (int)MathF.Round(t * timeline.DurationFrames), Value = i == 0 || i == samples ? 0 : amplitude * axis * (sustained ? 1 : 1 - t) * MathF.Sin(i * MathF.PI * .5f) });
            }
            timeline.Tracks.Add(track);
        }
    }
    public static IReadOnlyList<PrimitiveEntry> Shake(EntryCompileContext context, ShakePresetOptions options)
    {
        var timeline = Clip(options);
        Sustain(timeline, options.PlaybackMode);
        AddShake(timeline, Require(options.HandleId, "handleId"), options.Amplitude, options.Cycles, options.Angle, options.PlaybackMode == VisualPlaybackMode.UntilStopped);
        return Finish(context, timeline, options.BatchId);
    }
    private static void Sustain(TimelineDefinition timeline, VisualPlaybackMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("Unknown visual playback mode.");
        if (mode != VisualPlaybackMode.UntilStopped) return;
        timeline.LoopMode = AnimationLoopMode.Loop;
        timeline.Blocking = false;
        timeline.Skippable = false;
    }
    private static void AddLines(TimelineDefinition timeline, SpeedLinesPresetOptionsBase options)
    {
        if (options.Parameters.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Speed line parameters must be an object.");
        var layer = Id(timeline, "lines");
        var effect = Id(timeline, "lines-effect");
        var parameters = options.Parameters.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        parameters["time"] = Json(0f); parameters["speed"] = Json(options.Speed); parameters["flicker"] = Json(options.Flicker);
        timeline.Events.Add(Event(0, BuiltinEntryIds.ShowColorLayer, new { handleId = layer, color = "#00000000", z = options.Z, opacity = 1 }));
        timeline.Events.Add(Apply(0, effect, layer, options.Program, parameters));
        timeline.Tracks.Add(Track(effect, "time", timeline.DurationFrames, 0, (float)timeline.DurationFrames / timeline.FrameRate));
        var opacity = Track(layer, "opacity", timeline.DurationFrames, 0, 0);
        opacity.Keys.Insert(1, new() { Frame = timeline.DurationFrames / 2, Value = 1 });
        timeline.Tracks.Add(opacity);
        timeline.Events.Add(Stop(timeline.DurationFrames, effect));
        timeline.Events.Add(CrossFadeTransition.HideLayer(layer, timeline.DurationFrames));
    }
    public static IReadOnlyList<PrimitiveEntry> SpeedLines(EntryCompileContext context, SpeedLinesPresetOptions options)
    {
        var timeline = Clip(options);
        Sustain(timeline, options.PlaybackMode);
        AddLines(timeline, options);
        if (options.PlaybackMode == VisualPlaybackMode.UntilStopped)
        {
            timeline.Tracks.RemoveAll(track => track.Property == "opacity");
            foreach (var item in timeline.Events)
                item.Execution = item.Frame == 0 ? TimelineEventExecution.Once : TimelineEventExecution.OnCompletion;
        }
        return Finish(context, timeline, options.BatchId);
    }
    public static IReadOnlyList<PrimitiveEntry> Impact(EntryCompileContext context, ImpactPresetOptions options)
    {
        var timeline = Clip(options);
        var handle = Require(options.HandleId, "handleId");
        if (handle.StartsWith(options.PlaybackHandleId + "/visual/", StringComparison.Ordinal))
            throw new InvalidDataException("Impact target cannot use the playback's reserved /visual/ namespace.");
        AddShake(timeline, handle, options.Amplitude, options.Cycles, options.Angle);
        foreach (var property in new[] { "transform.scaleX", "transform.scaleY" })
            timeline.Tracks.Add(Pulse(handle, property, timeline.DurationFrames, .08f));
        AddLines(timeline, options);
        var flash = Id(timeline, "flash");
        timeline.Events.Add(Event(0, BuiltinEntryIds.ShowColorLayer, new { handleId = flash, color = "#FFFFFF", z = options.Z + 1, opacity = options.FlashOpacity }));
        var flashTrack = Track(flash, "opacity", timeline.DurationFrames, options.FlashOpacity, 0);
        flashTrack.Keys.Insert(1, new() { Frame = timeline.DurationFrames / 2, Value = 0 });
        timeline.Tracks.Add(flashTrack);
        timeline.Events.Add(CrossFadeTransition.HideLayer(flash, timeline.DurationFrames));
        return Finish(context, timeline, options.BatchId);
    }
}
