using GalNet.Core.Services;
using GalNet.Core.Entry;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Runtime.Handlers;

/// <summary>State-only context for one entry execution.</summary>
public sealed class EntryContext
{
    private static readonly JsonSerializerOptions LayerTransformJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions AnimationCurveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public required Entry Entry { get; init; }
    public required IGameRuntime Runtime { get; init; }
    /// <summary>Engine-owned dispatch for timeline events. It deliberately does not create checkpoints.</summary>
    public Func<Entry, CancellationToken, Task>? DispatchTimelineEventAsync { get; init; }

    public Dictionary<string, string> Params => Entry.Values;
    public ITextResolver TextResolver => Runtime.TextResolver;

    public string GetString(string key, string def = "") => Params.TryGetValue(key, out var value) ? value : def;
    public bool GetBool(string key, bool def = false) => Params.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : def;
    public float GetFloat(string key, float def = 0f) => Params.TryGetValue(key, out var value) && float.TryParse(value, out var result) ? result : def;
    public int GetInt(string key, int def = 0) => Params.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : def;
    public string GetText(string key, string def = "") => TextResolver.Resolve(GetString(key, def));

    public LayerTransform GetLayerTransform(string key = "transform")
    {
        var fallback = new LayerTransform();
        var raw = GetString(key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        try
        {
            var transform = JsonSerializer.Deserialize<LayerTransform>(raw, LayerTransformJsonOptions);
            if (transform is null || transform.ScaleX <= 0 || transform.ScaleY <= 0)
                throw new InvalidDataException("Layer scale values must be greater than zero.");
            return transform;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid layer transform: {raw}", exception);
        }
    }

    public AnimationRequest GetAnimation()
    {
        var property = GetString("property");
        if (string.IsNullOrWhiteSpace(property)) throw new InvalidDataException("Animation property is required.");
        if (!float.TryParse(GetString("to"), out var to)) throw new InvalidDataException("Animation target value is required.");
        if (GetFloat("duration", .25f) < 0) throw new InvalidDataException("Animation duration must not be negative.");
        if (!Enum.TryParse<BuiltinAnimationCurve>(GetString("curve", "Linear"), true, out var curve) || !Enum.IsDefined(curve))
            throw new InvalidDataException($"Unknown built-in animation curve '{GetString("curve")}'.");

        return new AnimationRequest
        {
            HandleId = GetString("handleId"), Property = property,
            From = GetOptionalFloat("from"),
            To = to, DurationSeconds = GetFloat("duration", .25f), Curve = AnimationCurves.Create(curve),
            Blocking = GetBool("blocking"), Skippable = GetBool("skippable"), BatchId = NullIfWhiteSpace(GetString("batchId"))
        };
    }

    public AnimationPlanDefinition GetAnimationPlan()
    {
        AnimationPlanDefinition plan;
        try
        {
            plan = JsonSerializer.Deserialize<AnimationPlanDefinition>(GetString("plan"), AnimationCurveJsonOptions)
                ?? throw new InvalidDataException("Animation plan is required.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Invalid animation plan.", exception);
        }

        ValidateAnimationPlan(plan);
        plan.BatchId = NullIfWhiteSpace(plan.BatchId);
        return plan;
    }

    private static void ValidateAnimationPlan(AnimationPlanDefinition plan)
    {
        if (plan.FrameRate is < 1 or > 240) throw new InvalidDataException("Animation plan frameRate must be between 1 and 240.");
        if (plan.DurationFrames < 0) throw new InvalidDataException("Animation plan durationFrames must not be negative.");
        if (plan.Tracks is null || plan.Events is null) throw new InvalidDataException("Animation plan tracks and events must be arrays.");

        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in plan.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.HandleId) || string.IsNullOrWhiteSpace(track.Property))
                throw new InvalidDataException("Every animation track requires handleId and property.");
            if (!properties.Add($"{track.HandleId}:{track.Property}"))
                throw new InvalidDataException($"Animation plan has duplicate track '{track.HandleId}:{track.Property}'.");
            if (track.Keys is null || track.Keys.Count == 0 || track.Keys[0].Frame != 0)
                throw new InvalidDataException($"Animation track '{track.HandleId}:{track.Property}' must start with a frame-0 key.");

            var previous = -1;
            foreach (var key in track.Keys)
            {
                if (key.Frame <= previous || key.Frame > plan.DurationFrames ||
                    !float.IsFinite(key.Value) || !float.IsFinite(key.InTangent) || !float.IsFinite(key.OutTangent) ||
                    !Enum.IsDefined(key.InterpolationToNext))
                    throw new InvalidDataException($"Animation track '{track.HandleId}:{track.Property}' has an invalid keyframe.");
                previous = key.Frame;
            }
        }

        foreach (var timelineEvent in plan.Events)
        {
            if (timelineEvent.Frame is < 0 || timelineEvent.Frame > plan.DurationFrames ||
                string.IsNullOrWhiteSpace(timelineEvent.Type) || timelineEvent.Parameters is null)
                throw new InvalidDataException("Animation plan has an invalid timeline event.");
        }
    }

    private float? GetOptionalFloat(string key)
    {
        var raw = GetString(key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return float.TryParse(raw, out var value)
            ? value
            : throw new InvalidDataException($"'{key}' must be a valid float.");
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

}
