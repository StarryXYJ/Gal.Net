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
        var result = JsonSerializer.SerializeToElement(root); _ = new TimelineDocument(result); return result;
    }
    public void ValidateMove(IEnumerable<TimelineKeyLocation> selected, int delta, bool replace)
    {
        var keys = Locations(selected);
        if (delta == 0) return;
        if (keys.Any(key => key.Frame == 0)) throw new InvalidDataException("Timeline.KeyProtected");
        if (keys.Any(key => (long)key.Frame + delta < 0 || (long)key.Frame + delta > Duration)) throw new InvalidDataException("Timeline.KeyOutside");
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
        return JsonSerializer.SerializeToElement(root);
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
    public JsonElement ChangeTiming(int duration, int fps, bool keepSeconds, bool trim)
    {
        if (duration < 0 || fps is < 1 or > 240) throw new InvalidDataException("Timeline.EditRejected");
        var ratio = keepSeconds ? (double)fps / FrameRate : 1;
        int Scale(int frame) => checked((int)Math.Round(frame * ratio, MidpointRounding.AwayFromZero));
        if (keepSeconds) duration = Scale(Duration);
        var root = (JsonObject)_root.DeepClone();
        var tracks = Array(root, "tracks");
        for (var trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
        {
            var keys = Array((JsonObject)tracks[trackIndex]!, "keys"); var original = Tracks[trackIndex].Keys; var frames = original.Select(key => Scale(key.Frame)).ToArray();
            if (frames.Distinct().Count() != frames.Length) throw new InvalidDataException("Timeline.KeyCollision");
            for (var index = keys.Count - 1; index >= 0; index--)
            {
                if (frames[index] > duration) { if (!trim) throw new InvalidDataException("Timeline.TrimRequired"); keys.RemoveAt(index); continue; }
                var key = (JsonObject)keys[index]!;
                if (keepSeconds)
                {
                    key[Name(key, "frame")] = frames[index];
                    void Tangent(string name, float value, int oldSpan, int newSpan)
                    {
                        var next = (float)((double)value * oldSpan / newSpan);
                        if (!float.IsFinite(next)) throw new InvalidDataException("Timeline.EditRejected");
                        // Preserve absence when the default zero has not changed.
                        if (value != next || key.ContainsKey(Name(key, name))) key[Name(key, name)] = next;
                    }
                    if (index > 0) Tangent("inTangent", original[index].InTangent, original[index].Frame - original[index - 1].Frame, frames[index] - frames[index - 1]);
                    if (index + 1 < original.Count) Tangent("outTangent", original[index].OutTangent, original[index + 1].Frame - original[index].Frame, frames[index + 1] - frames[index]);
                }
            }
        }
        var events = Array(root, "events");
        for (var index = events.Count - 1; index >= 0; index--)
        {
            if (events[index] is not JsonObject item) throw new InvalidDataException("Timeline.EditRejected");
            var previous = Integer(item, "frame", 0); if (previous < 0 || previous > Duration) throw new InvalidDataException("Timeline.EditRejected");
            var frame = Scale(previous);
            if (frame > duration) { if (!trim) throw new InvalidDataException("Timeline.TrimRequired"); events.RemoveAt(index); }
            else if (keepSeconds) item[Name(item, "frame")] = frame;
        }
        root[Name(root, "frameRate")] = fps; root[Name(root, "durationFrames")] = duration;
        var result = JsonSerializer.SerializeToElement(root); _ = new TimelineDocument(result); return result;
    }
}
