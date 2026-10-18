using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GalNet.Core.Scene;

namespace GalNet.Editor.Services;

/// <summary>Known-value projection of raw author JSON. Patches always retain the original objects.</summary>
internal sealed class TimelineDocument
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    private readonly JsonObject _root;
    public int Duration { get; }
    public int FrameRate { get; }
    public IReadOnlyList<TimelineTrackDefinition> Tracks { get; }
    public bool HasEvents { get; }
    public TimelineDocument(JsonElement value)
    {
        _root = JsonNode.Parse(value.GetRawText()) as JsonObject ?? throw new InvalidDataException("Timeline.Invalid");
        FrameRate = Integer(_root, "frameRate", 60);
        Duration = Integer(_root, "durationFrames", 0);
        HasEvents = Array(_root, "events").Count > 0;
        if (FrameRate is < 1 or > 240 || Duration < 0) throw new InvalidDataException("Timeline.Invalid");
        var tracks = Array(_root, "tracks");
        Tracks = tracks.Select(node => node?.Deserialize<TimelineTrackDefinition>(Options) ?? throw new InvalidDataException("Timeline.Invalid")).ToArray();
        var targets = new HashSet<(string, string)>();
        foreach (var track in Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.HandleId) || string.IsNullOrWhiteSpace(track.Property) || !Enum.IsDefined(track.BlendMode)
                || !targets.Add((track.HandleId, track.Property)) || track.Keys is not { Count: > 0 } || track.Keys[0] is null || track.Keys[0].Frame != 0)
                throw new InvalidDataException("Timeline.Invalid");
            var previous = -1;
            foreach (var key in track.Keys)
            {
                if (key is null || key.Frame <= previous || key.Frame > Duration || !Enum.IsDefined(key.InterpolationToNext)
                    || !float.IsFinite(key.Value) || !float.IsFinite(key.InTangent) || !float.IsFinite(key.OutTangent)) throw new InvalidDataException("Timeline.Invalid");
                previous = key.Frame;
            }
        }
    }
    private static string Name(JsonObject obj, string name)
    {
        var matches = obj.Select(pair => pair.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Timeline.Invalid");
        return matches.SingleOrDefault() ?? name;
    }
    private static int Integer(JsonObject obj, string name, int fallback) => obj.TryGetPropertyValue(Name(obj, name), out var value)
        ? value?.GetValue<int>() ?? throw new InvalidDataException("Timeline.Invalid") : fallback;
    private static JsonArray Array(JsonObject obj, string name) => !obj.TryGetPropertyValue(Name(obj, name), out var node) ? [] : node as JsonArray ?? throw new InvalidDataException("Timeline.Invalid");
    public JsonElement Add(string handle, string property, int frame, float value, float? baseline, bool additive)
    {
        if (Tracks.Any(track => track.HandleId == handle && track.Property == property) || string.IsNullOrWhiteSpace(handle)
            || frame < 0 || frame > Duration || !float.IsFinite(value)) throw new InvalidDataException("Timeline.Invalid");
        var initial = frame == 0 ? value : additive ? 0 : baseline ?? value;
        var root = (JsonObject)_root.DeepClone();
        var name = Name(root, "tracks");
        if (!root.ContainsKey(name)) root[name] = new JsonArray();
        var keys = new JsonArray(new JsonObject { ["frame"] = 0, ["value"] = initial, ["interpolationToNext"] = "Linear" });
        if (frame > 0) keys.Add(new JsonObject { ["frame"] = frame, ["value"] = value, ["interpolationToNext"] = "Linear" });
        Array(root, "tracks").Add(new JsonObject { ["handleId"] = handle, ["property"] = property, ["blendMode"] = additive ? "Additive" : "Replace", ["keys"] = keys });
        return JsonSerializer.SerializeToElement(root);
    }
    public JsonElement Remove(int index)
    {
        if (index < 0 || index >= Tracks.Count) throw new InvalidDataException("Timeline.Invalid");
        var root = (JsonObject)_root.DeepClone(); Array(root, "tracks").RemoveAt(index); return JsonSerializer.SerializeToElement(root);
    }
    public JsonElement Upsert(int index, int frame, float value)
    {
        if (index < 0 || index >= Tracks.Count || frame < 0 || frame > Duration || !float.IsFinite(value)) throw new InvalidDataException("Timeline.Invalid");
        var root = (JsonObject)_root.DeepClone();
        var track = (JsonObject)Array(root, "tracks")[index]!;
        var keys = Array(track, "keys");
        var definition = Tracks[index];
        var found = definition.Keys.FindIndex(key => key.Frame == frame);
        if (found >= 0)
        {
            var key = (JsonObject)keys[found]!;
            key[Name(key, "value")] = value;
        }
        else
        {
            var position = definition.Keys.FindIndex(key => key.Frame > frame);
            if (position < 0) position = keys.Count;
            var previous = definition.Keys[position - 1];
            // Derivative is in value/frame, matching the runtime Hermite sampler.
            var tangent = Derivative(definition, frame);
            keys.Insert(position, new JsonObject
            {
                ["frame"] = frame,
                ["value"] = value,
                ["inTangent"] = tangent,
                ["outTangent"] = tangent,
                ["interpolationToNext"] = previous.InterpolationToNext.ToString()
            });
        }
        return JsonSerializer.SerializeToElement(root);
    }
    private static float Derivative(TimelineTrackDefinition track, int frame)
    {
        if (frame >= track.Keys[^1].Frame) return 0;
        var endIndex = track.Keys.FindIndex(key => key.Frame > frame);
        var start = track.Keys[endIndex - 1]; var end = track.Keys[endIndex];
        var span = end.Frame - start.Frame; var t = (float)(frame - start.Frame) / span;
        return start.InterpolationToNext switch
        {
            AnimationInterpolation.Step => 0,
            AnimationInterpolation.Linear => (end.Value - start.Value) / span,
            _ => ((6 * t * t - 6 * t) * start.Value + (3 * t * t - 4 * t + 1) * start.OutTangent * span
                + (-6 * t * t + 6 * t) * end.Value + (3 * t * t - 2 * t) * end.InTangent * span) / span
        };
    }
}
