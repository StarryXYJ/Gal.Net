using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Contracts.Builtins.Scene;

namespace GalNet.Contracts.Builtins;

/// <summary>Portable semantic checks shared by compilation and every implementation of built-in contracts.</summary>
internal static class BuiltinArgumentValidation
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Validate(string type, JsonElement arguments)
    {
        switch (type)
        {
            case BuiltinEntryIds.StopAudioSEffect:
                if (arguments.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.True &&
                    arguments.TryGetProperty("instanceId", out var instance) && instance.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(instance.GetString()))
                    throw new ArgumentException("Stopping all audio effects cannot specify an instance.");
                break;
            case BuiltinEntryIds.PlayVideo:
                RequireText(type, arguments, "asset", "handleId");
                var points = arguments.GetProperty("skipPoints").Deserialize<double[]>(JsonOptions)
                    ?? throw new InvalidDataException("video.play skipPoints must be an array.");
                if (points.Any(point => !double.IsFinite(point) || point < 0) ||
                    points.Zip(points.Skip(1), (left, right) => right > left).Any(valid => !valid))
                    throw new InvalidDataException("video.play skipPoints must be finite, non-negative and strictly increasing seconds.");
                break;
            case BuiltinEntryIds.PlayTimeline:
                if (!arguments.TryGetProperty("timeline", out var value) || value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("timeline.play requires a timeline object.");
                ValidateTimeline(value.Deserialize<TimelineDefinition>(JsonOptions)!);
                break;
            case BuiltinEntryIds.PlayParticleEmitter:
            case BuiltinEntryIds.BurstParticles:
                if (type == BuiltinEntryIds.PlayParticleEmitter) RequireText(type, arguments, "instanceId");
                var definition = ParticleEmitterDefinition.FromJson(arguments.GetProperty("parameters").GetRawText());
                if (string.IsNullOrWhiteSpace(definition.ParticleTexture))
                    throw new InvalidDataException($"{type} requires 'parameters.particleTexture'.");
                break;
        }
    }

    private static void RequireText(string type, JsonElement arguments, params string[] names)
    {
        foreach (var name in names)
            if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidDataException($"{type} requires '{name}'.");
    }

    private static void ValidateTimeline(TimelineDefinition timeline)
    {
        ValidateTimelineStructure(timeline);
        foreach (var item in timeline.Events) TimelineEventCapabilities.NormalizeEvent(item);
    }
    internal static void ValidateTimelineStructure(TimelineDefinition timeline)
    {
        if (timeline is null) throw new InvalidDataException("Missing Timeline definition.");
        if (timeline.FrameRate is < 1 or > 240 || timeline.DurationFrames < 0)
            throw new InvalidDataException("Animation timeline frameRate must be 1..240 and durationFrames must be non-negative.");
        if (timeline.Tracks is null || timeline.Events is null || !Enum.IsDefined(timeline.LoopMode))
            throw new InvalidDataException("Animation timeline requires track/event arrays and a valid loop mode.");
        var targets = new HashSet<(string HandleId, string Property)>();
        foreach (var track in timeline.Tracks)
        {
            if (track is null || track.Keys is null || !Enum.IsDefined(track.BlendMode) ||
                string.IsNullOrWhiteSpace(track.HandleId) || string.IsNullOrWhiteSpace(track.Property) ||
                !targets.Add((track.HandleId, track.Property)) || track.Keys.Count == 0 || track.Keys[0] is null || track.Keys[0].Frame != 0)
                throw new InvalidDataException("Animation tracks require unique targets and a key at frame zero.");
            var previous = -1;
            foreach (var key in track.Keys)
            {
                if (key is null || !Enum.IsDefined(key.InterpolationToNext) || key.Frame <= previous || key.Frame > timeline.DurationFrames ||
                    !float.IsFinite(key.Value) || !float.IsFinite(key.InTangent) || !float.IsFinite(key.OutTangent))
                    throw new InvalidDataException("Animation keys must be finite, strictly ordered and inside the timeline duration.");
                previous = key.Frame;
            }
        }
        foreach (var item in timeline.Events)
        {
            if (item is null || item.Parameters is null || item.Frame < 0 || item.Frame > timeline.DurationFrames || !Enum.IsDefined(item.Execution))
                throw new InvalidDataException("Animation events must be inside the timeline duration.");
            if (item.Execution == TimelineEventExecution.Once && item.Frame != 0 ||
                item.Execution == TimelineEventExecution.OnCompletion && (item.Frame != timeline.DurationFrames ||
                    item.Type is not (BuiltinEntryIds.HideLayer or BuiltinEntryIds.StopEffect or BuiltinEntryIds.StopParticleEmitter)))
                throw new InvalidDataException("Playback setup must be at frame zero; completion events must be terminal cleanup operations.");
        }
    }
}
