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

    /// <summary>Builds and validates an immediate <c>animate</c> request from the current entry.</summary>
    /// <returns>A request with a unique playback handle and legal loop/blocking combination.</returns>
    /// <exception cref="InvalidDataException">A required parameter is absent or the request violates animation constraints.</exception>
    public AnimationRequest GetAnimation()
    {
        var playbackHandleId = GetString("playbackHandleId");
        if (string.IsNullOrWhiteSpace(playbackHandleId)) throw new InvalidDataException("Animation playbackHandleId is required.");
        var property = GetString("property");
        if (string.IsNullOrWhiteSpace(property)) throw new InvalidDataException("Animation property is required.");
        if (!float.TryParse(GetString("to"), out var to) || !float.IsFinite(to)) throw new InvalidDataException("Animation target value is required.");
        if (GetFloat("duration", .25f) < 0) throw new InvalidDataException("Animation duration must not be negative.");
        if (!Enum.TryParse<BuiltinAnimationCurve>(GetString("curve", "Linear"), true, out var curve) || !Enum.IsDefined(curve))
            throw new InvalidDataException($"Unknown built-in animation curve '{GetString("curve")}'.");
        if (!Enum.TryParse<AnimationLoopMode>(GetString("loopMode", "Once"), true, out var loopMode) || !Enum.IsDefined(loopMode))
            throw new InvalidDataException($"Unknown animation loopMode '{GetString("loopMode")}'.");
        if (!Enum.TryParse<AnimationBlendMode>(GetString("blendMode", "Replace"), true, out var blendMode) || !Enum.IsDefined(blendMode))
            throw new InvalidDataException($"Unknown animation blendMode '{GetString("blendMode")}'.");
        var blocking = GetBool("blocking");
        var skippable = GetBool("skippable");
        if (loopMode != AnimationLoopMode.Once && (blocking || skippable))
            throw new InvalidDataException("Loop and PingPong animations must be non-blocking and non-skippable.");
        if (loopMode != AnimationLoopMode.Once && GetFloat("duration", .25f) <= 0)
            throw new InvalidDataException("Loop and PingPong animations require a positive duration.");

        var from = GetOptionalFloat("from");
        if (from is { } start && !float.IsFinite(start)) throw new InvalidDataException("Animation start value must be finite.");

        return new AnimationRequest
        {
            PlaybackHandleId = playbackHandleId, HandleId = GetString("handleId"), Property = property,
            From = from,
            To = to, DurationSeconds = GetFloat("duration", .25f), Curve = AnimationCurves.Create(curve),
            Blocking = blocking, Skippable = skippable, BatchId = NullIfWhiteSpace(GetString("batchId")), LoopMode = loopMode,
            BlendMode = blendMode
        };
    }

    /// <summary>Deserializes and validates an <c>animation.play</c> keyframe timeline.</summary>
    /// <returns>A plan whose tracks are frame-ordered, property-unique and safe for Runtime dispatch.</returns>
    /// <exception cref="InvalidDataException">The plan JSON or any timeline invariant is invalid.</exception>
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
        if (string.IsNullOrWhiteSpace(plan.PlaybackHandleId)) throw new InvalidDataException("Animation plan playbackHandleId is required.");
        if (plan.FrameRate is < 1 or > 240) throw new InvalidDataException("Animation plan frameRate must be between 1 and 240.");
        if (plan.DurationFrames < 0) throw new InvalidDataException("Animation plan durationFrames must not be negative.");
        if (plan.Tracks is null || plan.Events is null) throw new InvalidDataException("Animation plan tracks and events must be arrays.");
        if (!Enum.IsDefined(plan.LoopMode)) throw new InvalidDataException("Animation plan loopMode is invalid.");
        if (plan.LoopMode == AnimationLoopMode.PingPong)
            throw new InvalidDataException("PingPong is supported by animate but not animation plans.");
        if (plan.LoopMode == AnimationLoopMode.Loop && (plan.Blocking || plan.Skippable))
            throw new InvalidDataException("Loop animation plans must be non-blocking and non-skippable.");
        if (plan.LoopMode == AnimationLoopMode.Loop && plan.DurationFrames == 0)
            throw new InvalidDataException("Loop animation plans require a positive durationFrames value.");

        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in plan.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.HandleId) || string.IsNullOrWhiteSpace(track.Property))
                throw new InvalidDataException("Every animation track requires handleId and property.");
            if (!Enum.IsDefined(track.BlendMode))
                throw new InvalidDataException($"Animation track '{track.HandleId}:{track.Property}' has an invalid blendMode.");
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
