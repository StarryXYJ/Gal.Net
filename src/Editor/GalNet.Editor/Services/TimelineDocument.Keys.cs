using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Scene;

namespace GalNet.Editor.Services;

internal readonly record struct TimelineKeyLocation(int Track, int Frame);

internal sealed partial class TimelineDocument
{
    private TimelineKeyLocation[] Locations(IEnumerable<TimelineKeyLocation> selected)
    {
        var keys = selected.Distinct().ToArray();
        if (keys.Any(key => key.Track < 0 || key.Track >= Tracks.Count || !Tracks[key.Track].Keys.Any(item => item.Frame == key.Frame))) throw new InvalidDataException("Timeline.EditRejected");
        return keys;
    }
    public JsonElement MoveKeys(IEnumerable<TimelineKeyLocation> selected, int delta, bool replace)
    {
        var keys = Locations(selected);
        ValidateMove(keys, delta, replace);
        if (delta == 0) return JsonSerializer.SerializeToElement(_root);
        var root = (JsonObject)_root.DeepClone();
        foreach (var group in keys.GroupBy(key => key.Track))
        {
            var frames = group.Select(key => key.Frame).ToHashSet(); var destinations = frames.Select(frame => frame + delta).ToHashSet();
            var track = (JsonObject)Array(root, "tracks")[group.Key]!; var array = Array(track, "keys");
            var nodes = array.OfType<JsonObject>().Where(key => !replace || frames.Contains(Integer(key, "frame", 0)) || !destinations.Contains(Integer(key, "frame", 0))).Select(key => (JsonObject)key.DeepClone()).ToArray();
            foreach (var key in nodes) { var frame = Integer(key, "frame", 0); if (frames.Contains(frame)) key[Name(key, "frame")] = frame + delta; }
            array.Clear(); foreach (var key in nodes.OrderBy(key => Integer(key, "frame", 0))) array.Add(key);
        }
        root[Name(root, "durationFrames")] = Math.Max(Duration, keys.Select(key => key.Frame + delta).DefaultIfEmpty(0).Max());
        return FinishCurvePatch(root);
    }
    public void ValidateMove(IEnumerable<TimelineKeyLocation> selected, int delta, bool replace)
    {
        var keys = Locations(selected);
        if (delta == 0) return;
        if (keys.Any(key => key.Frame == 0)) throw new InvalidDataException("Timeline.KeyProtected");
        if (keys.Any(key => (long)key.Frame + delta < 0 || (long)key.Frame + delta > int.MaxValue)) throw new InvalidDataException("Timeline.KeyOutside");
        foreach (var group in keys.GroupBy(key => key.Track))
        {
            var frames = group.Select(key => key.Frame).ToHashSet(); var destinations = frames.Select(frame => frame + delta).ToHashSet();
            var collision = Tracks[group.Key].Keys.Where(key => !frames.Contains(key.Frame) && destinations.Contains(key.Frame)).ToArray();
            if (collision.Any(key => key.Frame == 0)) throw new InvalidDataException("Timeline.KeyProtected");
            if (!replace && collision.Length != 0) throw new InvalidDataException("Timeline.KeyCollision");
        }
    }
    public JsonElement DeleteKeys(IEnumerable<TimelineKeyLocation> selected)
    {
        var keys = Locations(selected);
        if (keys.Any(key => key.Frame == 0)) throw new InvalidDataException("Timeline.KeyProtected");
        var root = (JsonObject)_root.DeepClone();
        foreach (var group in keys.GroupBy(key => key.Track))
        {
            var frames = group.Select(key => key.Frame).ToHashSet(); var array = Array((JsonObject)Array(root, "tracks")[group.Key]!, "keys");
            for (var index = array.Count - 1; index >= 0; index--) if (frames.Contains(Integer((JsonObject)array[index]!, "frame", 0))) array.RemoveAt(index);
        }
        return FinishCurvePatch(root);
    }
    public JsonElement AddKeys(IEnumerable<int> tracks, int frame)
    {
        var result = JsonSerializer.SerializeToElement(_root);
        foreach (var index in tracks.Distinct())
        {
            if (index < 0 || index >= Tracks.Count) throw new InvalidDataException("Timeline.EditRejected");
            if (!Tracks[index].Keys.Any(key => key.Frame == frame)) result = new TimelineDocument(result).Upsert(index, frame, TimelineSampler.Evaluate(Tracks[index], frame));
        }
        return result;
    }
    public JsonElement ChangeTiming(int duration, int fps)
    {
        if (duration < 0 || fps is < 1 or > 240) throw new InvalidDataException("Timeline.EditRejected");
        var root = (JsonObject)_root.DeepClone();
        if (fps != FrameRate) root[Name(root, "frameRate")] = fps;
        duration = Math.Max(duration, MinimumDuration);
        if (duration != Duration) root[Name(root, "durationFrames")] = duration;
        return JsonSerializer.SerializeToElement(root);
    }
    public JsonElement ChangeBlendMode(int index, bool additive)
    {
        if (index < 0 || index >= Tracks.Count) throw new InvalidDataException("Timeline.EditRejected");
        if ((Tracks[index].BlendMode == AnimationBlendMode.Additive) == additive) return JsonSerializer.SerializeToElement(_root);
        var root = (JsonObject)_root.DeepClone();
        var track = (JsonObject)Array(root, "tracks")[index]!;
        track[Name(track, "blendMode")] = additive ? "Additive" : "Replace";
        return JsonSerializer.SerializeToElement(root);
    }
}
