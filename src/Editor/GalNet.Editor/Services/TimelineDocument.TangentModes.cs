using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Scene;
using GalNet.Game.Controls.Timeline;

namespace GalNet.Editor.Services;

public enum TimelineTangentMode { Free, Flat, Linear, Auto }
public sealed record TimelineTangentSettings(TimelineTangentMode Incoming, TimelineTangentMode Outgoing, bool Linked);

internal sealed partial class TimelineDocument
{
    private static TimelineTangentSettings Settings(JsonObject key)
    {
        if (!key.TryGetPropertyValue(Name(key, "editorTangents"), out var node)) return new(TimelineTangentMode.Free, TimelineTangentMode.Free, false);
        if (node is not JsonObject modes) throw new InvalidDataException("Timeline.Invalid");
        TimelineTangentMode Read(string name) => !modes.TryGetPropertyValue(Name(modes, name), out var value) ? TimelineTangentMode.Free
            : value is JsonValue item && item.TryGetValue<string>(out var text) && Enum.TryParse<TimelineTangentMode>(text, out var mode) && Enum.IsDefined(mode)
                ? mode : throw new InvalidDataException("Timeline.Invalid");
        var linked = modes.TryGetPropertyValue(Name(modes, "linked"), out var flag) && (flag?.GetValue<bool>() ?? throw new InvalidDataException("Timeline.Invalid"));
        return new(Read("incoming"), Read("outgoing"), linked);
    }
    public TimelineTangentSettings TangentSettings(TimelineKeyLocation location)
    {
        var index = Tracks[location.Track].Keys.FindIndex(key => key.Frame == location.Frame);
        return Settings((JsonObject)Array((JsonObject)Array(_root, "tracks")[location.Track]!, "keys")[index]!);
    }
    private static void WriteSettings(JsonObject key, TimelineTangentSettings settings)
    {
        var name = Name(key, "editorTangents");
        if (!key.ContainsKey(name)) key[name] = new JsonObject();
        var modes = key[name] as JsonObject ?? throw new InvalidDataException("Timeline.Invalid");
        modes[Name(modes, "incoming")] = settings.Incoming.ToString();
        modes[Name(modes, "outgoing")] = settings.Outgoing.ToString();
        modes[Name(modes, "linked")] = settings.Linked;
    }
    private static void RecalculateTangents(JsonObject root)
    {
        foreach (var track in Array(root, "tracks").OfType<JsonObject>())
        {
            var keys = Array(track, "keys");
            var definitions = keys.Select(key => key!.Deserialize<TimelineKeyframeDefinition>(Options)!).ToArray();
            for (var index = 0; index < keys.Count; index++)
            {
                var raw = (JsonObject)keys[index]!; var settings = Settings(raw);
                var current = definitions[index]; var previous = definitions[Math.Max(0, index - 1)]; var next = definitions[Math.Min(keys.Count - 1, index + 1)];
                double Slope(TimelineKeyframeDefinition left, TimelineKeyframeDefinition right) => left.Frame == right.Frame ? 0 : ((double)right.Value - left.Value) / (right.Frame - left.Frame);
                var left = index > 0 ? Slope(previous, current) : Slope(current, next);
                var right = index + 1 < keys.Count ? Slope(current, next) : left;
                var automatic = Slope(previous, next);
                if (index > 0 && index + 1 < keys.Count)
                    automatic = Math.Sign(left) != Math.Sign(right) ? 0 : Math.CopySign(Math.Min(Math.Abs(automatic), 3 * Math.Min(Math.Abs(left), Math.Abs(right))), automatic);
                void Set(string field, TimelineTangentMode mode, double linear)
                {
                    if (mode == TimelineTangentMode.Free) return;
                    var value = (float)(mode == TimelineTangentMode.Flat ? 0 : mode == TimelineTangentMode.Linear ? linear : automatic);
                    if (!float.IsFinite(value)) throw new InvalidDataException("Timeline.EditRejected");
                    raw[Name(raw, field)] = value;
                }
                Set("inTangent", settings.Incoming, left); Set("outTangent", settings.Outgoing, right);
            }
        }
    }
    private static JsonElement FinishCurvePatch(JsonObject root)
    {
        RecalculateTangents(root);
        var result = JsonSerializer.SerializeToElement(root); _ = new TimelineDocument(result); return result;
    }
    public JsonElement SetTangentMode(IEnumerable<TimelineKeyLocation> selected, bool? incoming, TimelineTangentMode mode, bool? linked = null)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("Timeline.EditRejected");
        var root = (JsonObject)_root.DeepClone();
        foreach (var location in Locations(selected))
        {
            var keys = Array((JsonObject)Array(root, "tracks")[location.Track]!, "keys");
            var index = Tracks[location.Track].Keys.FindIndex(key => key.Frame == location.Frame); var key = (JsonObject)keys[index]!;
            var settings = Settings(key);
            if (linked is { } joining)
            {
                settings = settings with { Linked = joining };
                if (joining)
                {
                    var definition = Tracks[location.Track].Keys[index];
                    var tangent = (float)(((double)definition.InTangent + definition.OutTangent) / 2);
                    key[Name(key, "inTangent")] = tangent; key[Name(key, "outTangent")] = tangent;
                    settings = new(TimelineTangentMode.Free, TimelineTangentMode.Free, true);
                }
            }
            else
            {
                settings = new(incoming != false ? mode : settings.Incoming, incoming != true ? mode : settings.Outgoing, incoming is null && mode is TimelineTangentMode.Auto or TimelineTangentMode.Flat);
                if (incoming != false && index > 0) ((JsonObject)keys[index - 1]!)[Name((JsonObject)keys[index - 1]!, "interpolationToNext")] = "CubicHermite";
                if (incoming != true) key[Name(key, "interpolationToNext")] = "CubicHermite";
            }
            WriteSettings(key, settings);
        }
        return FinishCurvePatch(root);
    }
    public JsonElement ChangeCurve(IEnumerable<TimelineKeyLocation> selected, TimelineCurveGesture gesture)
    {
        var locations = Locations(selected);
        var moved = gesture.FrameDelta == 0 ? this : new TimelineDocument(MoveKeys(locations, gesture.FrameDelta, false));
        var root = (JsonObject)moved._root.DeepClone();
        foreach (var location in locations)
        {
            var keys = Array((JsonObject)Array(root, "tracks")[location.Track]!, "keys");
            var index = moved.Tracks[location.Track].Keys.FindIndex(key => key.Frame == location.Frame + gesture.FrameDelta); var raw = (JsonObject)keys[index]!;
            var definition = moved.Tracks[location.Track].Keys[index];
            if (gesture.Tangent is { } tangent)
            {
                if (!float.IsFinite(tangent)) throw new InvalidDataException("Timeline.EditRejected");
                var settings = Settings(raw);
                var incoming = gesture.Incoming || settings.Linked; var outgoing = !gesture.Incoming || settings.Linked;
                if (incoming) { raw[Name(raw, "inTangent")] = tangent; if (index > 0) ((JsonObject)keys[index - 1]!)[Name((JsonObject)keys[index - 1]!, "interpolationToNext")] = "CubicHermite"; }
                if (outgoing) { raw[Name(raw, "outTangent")] = tangent; raw[Name(raw, "interpolationToNext")] = "CubicHermite"; }
                WriteSettings(raw, settings with { Incoming = incoming ? TimelineTangentMode.Free : settings.Incoming, Outgoing = outgoing ? TimelineTangentMode.Free : settings.Outgoing });
            }
            else
            {
                var value = (float)((double)definition.Value + gesture.ValueDelta);
                if (!float.IsFinite(value)) throw new InvalidDataException("Timeline.EditRejected");
                raw[Name(raw, "value")] = value;
            }
        }
        return FinishCurvePatch(root);
    }
}
