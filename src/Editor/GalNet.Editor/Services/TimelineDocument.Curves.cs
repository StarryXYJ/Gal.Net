using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Scene;

namespace GalNet.Editor.Services;

public enum TimelineTangentPreset { Flat, Linear, Smooth, Aligned }

internal sealed partial class TimelineDocument
{
    public JsonElement EditKeys(IEnumerable<TimelineKeyLocation> selected, float? value = null, float? incoming = null,
        float? outgoing = null, AnimationInterpolation? interpolation = null, TimelineTangentPreset? preset = null)
    {
        var locations = Locations(selected);
        if (locations.Length == 0 || new[] { value, incoming, outgoing }.Any(number => number is { } item && !float.IsFinite(item))
            || interpolation is { } mode && !Enum.IsDefined(mode) || preset is { } setting && !Enum.IsDefined(setting)) throw new InvalidDataException("Timeline.EditRejected");
        var root = (JsonObject)_root.DeepClone();
        foreach (var location in locations)
        {
            var definition = Tracks[location.Track]; var index = definition.Keys.FindIndex(key => key.Frame == location.Frame);
            var key = (JsonObject)Array((JsonObject)Array(root, "tracks")[location.Track]!, "keys")[index]!;
            void Set(string name, float number) { if (!float.IsFinite(number)) throw new InvalidDataException("Timeline.EditRejected"); key[Name(key, name)] = number; }
            var settings = Settings(key);
            if (incoming is not null || outgoing is not null || preset is not null)
            {
                var inputChanged = incoming is not null || preset is not null || settings.Linked && outgoing is not null;
                var outputChanged = outgoing is not null || preset is not null || settings.Linked && incoming is not null;
                if (key.ContainsKey(Name(key, "editorTangents")))
                    WriteSettings(key, settings with { Incoming = inputChanged ? TimelineTangentMode.Free : settings.Incoming, Outgoing = outputChanged ? TimelineTangentMode.Free : settings.Outgoing });
                if (settings.Linked && preset is null)
                { if (incoming is { } joinedIn) Set("outTangent", joinedIn); else if (outgoing is { } joinedOut) Set("inTangent", joinedOut); }
            }
            if (value is { } number) Set("value", number);
            if (incoming is { } input) Set("inTangent", input);
            if (outgoing is { } output) Set("outTangent", output);
            if (interpolation is { } kind) key[Name(key, "interpolationToNext")] = kind.ToString();
            if (preset is { } action)
            {
                var current = definition.Keys[index]; var previous = index > 0 ? definition.Keys[index - 1] : current;
                var next = index + 1 < definition.Keys.Count ? definition.Keys[index + 1] : current;
                float Slope(TimelineKeyframeDefinition left, TimelineKeyframeDefinition right) => left.Frame == right.Frame ? 0 : (float)(((double)right.Value - left.Value) / (right.Frame - left.Frame));
                var leftSlope = index > 0 ? Slope(previous, current) : Slope(current, next);
                var rightSlope = index + 1 < definition.Keys.Count ? Slope(current, next) : leftSlope;
                var smooth = Slope(previous, next);
                var aligned = (float)(((double)current.InTangent + current.OutTangent) / 2);
                Set("inTangent", action switch { TimelineTangentPreset.Flat => 0, TimelineTangentPreset.Linear => leftSlope, TimelineTangentPreset.Smooth => smooth, _ => aligned });
                Set("outTangent", action switch { TimelineTangentPreset.Flat => 0, TimelineTangentPreset.Linear => rightSlope, TimelineTangentPreset.Smooth => smooth, _ => aligned });
                key[Name(key, "interpolationToNext")] = AnimationInterpolation.CubicHermite.ToString();
            }
        }
        return FinishCurvePatch(root);
    }
}
