using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Scene;

namespace GalNet.Core.Entry;

internal static class TransitionEntrySupport
{
    public static readonly JsonSerializerOptions PlanJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions TransformJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Get(Entry entry, string name, string fallback) => entry.Values.TryGetValue(name, out var value) ? value : fallback;
    public static string Require(Entry entry, string name)
    {
        var value = Get(entry, name, "");
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"Transition '{entry.Type}' requires '{name}'.");
    }
    public static int PositiveInt(Entry entry, string name) => int.TryParse(Get(entry, name, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
        ? value : throw new InvalidDataException($"Transition '{entry.Type}' requires a positive integer '{name}'.");
    public static float Float(Entry entry, string name) => float.TryParse(Get(entry, name, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
        ? value : throw new InvalidDataException($"Transition '{entry.Type}' requires a numeric '{name}'.");
    public static bool Bool(Entry entry, string name) => bool.TryParse(Get(entry, name, "false"), out var value)
        ? value : throw new InvalidDataException($"Transition '{entry.Type}' requires a boolean '{name}'.");
    public static JsonElement Json(Entry entry, string name, string fallback)
    {
        try { using var document = JsonDocument.Parse(Get(entry, name, fallback)); return document.RootElement.Clone(); }
        catch (JsonException exception) { throw new InvalidDataException($"Transition '{entry.Type}' has invalid JSON '{name}'.", exception); }
    }
    public static LayerDisplayMode DisplayMode(Entry entry, string name, string fallback)
    {
        var value = Get(entry, name, fallback);
        return Enum.TryParse<LayerDisplayMode>(value, true, out var mode) && Enum.IsDefined(mode)
            ? mode : throw new InvalidDataException($"Unknown layer displayMode '{value}'.");
    }
    public static LayerTransform Transform(Entry entry, string name, string fallback)
    {
        try
        {
            var transform = JsonSerializer.Deserialize<LayerTransform>(Get(entry, name, fallback), TransformJsonOptions);
            return transform is { ScaleX: > 0, ScaleY: > 0 } ? transform : throw new InvalidDataException();
        }
        catch (JsonException exception) { throw new InvalidDataException($"Transition '{entry.Type}' has invalid transform '{name}'.", exception); }
        catch (InvalidDataException) { throw new InvalidDataException($"Transition '{entry.Type}' requires positive scale values in '{name}'."); }
    }
    public static PrimitiveEntry PlanEntry(EntryCompileContext context, AnimationPlanDefinition plan)
    {
        var primitive = context.CreatePrimitive<PlayAnimationPlanEntry>();
        primitive.Values["plan"] = JsonSerializer.Serialize(plan, PlanJsonOptions);
        return primitive;
    }
    public static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Non-primitive cross-fade that compiles into one keyframe animation plan.</summary>
public sealed class CrossFadeTransitionEntry : NonPrimitiveEntry
{
    public const string TypeId = "transition.crossFade";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("playbackHandleId", EntryParameterType.Text), ("oldHandleId", EntryParameterType.Text), ("newHandleId", EntryParameterType.Text), ("assetId", EntryParameterType.ImageAsset), ("transform", EntryParameterType.Json), ("z", EntryParameterType.Float), ("displayMode", EntryParameterType.Select), ("frameRate", EntryParameterType.Integer), ("durationFrames", EntryParameterType.Integer), ("blocking", EntryParameterType.Select), ("skippable", EntryParameterType.Select), ("batchId", EntryParameterType.Text));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transform", "{}"), ("z", "0"), ("displayMode", "Fill"), ("frameRate", "60"), ("durationFrames", "48"), ("blocking", "false"), ("skippable", "true"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("displayMode", ["Native", "Tile", "Fill", "Uniform", "UniformToFill"]), ("blocking", ["false", "true"]), ("skippable", ["false", "true"]));

    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context)
    {
        var playback = TransitionEntrySupport.Require(this, "playbackHandleId");
        var oldHandle = TransitionEntrySupport.Require(this, "oldHandleId");
        var newHandle = TransitionEntrySupport.Require(this, "newHandleId");
        var asset = TransitionEntrySupport.Require(this, "assetId");
        var frames = TransitionEntrySupport.PositiveInt(this, "durationFrames");
        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = playback, FrameRate = TransitionEntrySupport.PositiveInt(this, "frameRate"), DurationFrames = frames,
            Blocking = TransitionEntrySupport.Bool(this, "blocking"), Skippable = TransitionEntrySupport.Bool(this, "skippable"), BatchId = TransitionEntrySupport.NullIfWhiteSpace(TransitionEntrySupport.Get(this, "batchId", "")),
            Tracks = [OpacityTrack(oldHandle, 1, 0, frames), OpacityTrack(newHandle, 0, 1, frames)],
            Events = [ShowLayer(newHandle, asset, TransitionEntrySupport.Json(this, "transform", "{}"), TransitionEntrySupport.Float(this, "z"), 0, TransitionEntrySupport.DisplayMode(this, "displayMode", "Fill")), HideLayer(oldHandle, frames)]
        };
        return [TransitionEntrySupport.PlanEntry(context, plan)];
    }

    internal static AnimationTrackDefinition OpacityTrack(string handle, float from, float to, int frames) => new()
    {
        HandleId = handle, Property = "opacity",
        Keys = [new AnimationKeyframeDefinition { Frame = 0, Value = from, InterpolationToNext = AnimationInterpolation.Linear }, new AnimationKeyframeDefinition { Frame = frames, Value = to }]
    };
    internal static AnimationPlanEventDefinition ShowLayer(string handle, string asset, JsonElement transform, float z, float opacity, LayerDisplayMode displayMode) => new()
    {
        Frame = 0, Type = ShowLayerEntry.TypeId,
        Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["handleId"] = JsonSerializer.SerializeToElement(handle), ["assetId"] = JsonSerializer.SerializeToElement(asset), ["transform"] = transform, ["z"] = JsonSerializer.SerializeToElement(z), ["opacity"] = JsonSerializer.SerializeToElement(opacity), ["displayMode"] = JsonSerializer.SerializeToElement(displayMode.ToString()) }
    };
    internal static AnimationPlanEventDefinition HideLayer(string handle, int frame) => new()
    {
        Frame = frame, Type = HideLayerEntry.TypeId,
        Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["handleId"] = JsonSerializer.SerializeToElement(handle) }
    };
}

/// <summary>Slides in the replacement layer and moves the outgoing layer by the same distance.</summary>
public sealed class SlideTransitionEntry : NonPrimitiveEntry
{
    private enum Direction { Left, Right, Up, Down }

    public const string TypeId = "transition.slide";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("playbackHandleId", EntryParameterType.Text), ("fromLayerHandleId", EntryParameterType.Text), ("toLayerHandleId", EntryParameterType.Text), ("toAssetId", EntryParameterType.ImageAsset), ("toTransform", EntryParameterType.Json), ("toZ", EntryParameterType.Float), ("toDisplayMode", EntryParameterType.Select), ("direction", EntryParameterType.Select), ("distance", EntryParameterType.Float), ("frameRate", EntryParameterType.Integer), ("durationFrames", EntryParameterType.Integer), ("blocking", EntryParameterType.Select), ("skippable", EntryParameterType.Select), ("batchId", EntryParameterType.Text));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("toTransform", "{}"), ("toZ", "0"), ("toDisplayMode", "Fill"), ("direction", "Left"), ("distance", "1920"), ("frameRate", "60"), ("durationFrames", "30"), ("blocking", "false"), ("skippable", "true"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("toDisplayMode", ["Native", "Tile", "Fill", "Uniform", "UniformToFill"]), ("direction", ["Left", "Right", "Up", "Down"]), ("blocking", ["false", "true"]), ("skippable", ["false", "true"]));

    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context)
    {
        var playback = TransitionEntrySupport.Require(this, "playbackHandleId");
        var oldHandle = TransitionEntrySupport.Require(this, "fromLayerHandleId");
        var newHandle = TransitionEntrySupport.Require(this, "toLayerHandleId");
        var destination = TransitionEntrySupport.Transform(this, "toTransform", "{}");
        var distance = TransitionEntrySupport.Float(this, "distance");
        if (distance <= 0) throw new InvalidDataException($"Transition '{TypeId}' requires a positive 'distance'.");
        if (!Enum.TryParse<Direction>(TransitionEntrySupport.Get(this, "direction", "Left"), true, out var direction) || !Enum.IsDefined(direction))
            throw new InvalidDataException($"Transition '{TypeId}' has an unknown direction.");
        var (incomingX, incomingY) = direction switch { Direction.Left => (distance, 0f), Direction.Right => (-distance, 0f), Direction.Up => (0f, distance), Direction.Down => (0f, -distance), _ => (0f, 0f) };
        var initial = destination.Clone(); initial.X += incomingX; initial.Y += incomingY;
        var frames = TransitionEntrySupport.PositiveInt(this, "durationFrames");
        var property = incomingX != 0 ? "transform.x" : "transform.y";
        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = playback, FrameRate = TransitionEntrySupport.PositiveInt(this, "frameRate"), DurationFrames = frames,
            Blocking = TransitionEntrySupport.Bool(this, "blocking"), Skippable = TransitionEntrySupport.Bool(this, "skippable"), BatchId = TransitionEntrySupport.NullIfWhiteSpace(TransitionEntrySupport.Get(this, "batchId", "")),
            Tracks = [Track(oldHandle, property, 0, -incomingX - incomingY, frames, AnimationBlendMode.Additive), Track(newHandle, property, incomingX != 0 ? initial.X : initial.Y, incomingX != 0 ? destination.X : destination.Y, frames, AnimationBlendMode.Replace)],
            Events = [CrossFadeTransitionEntry.ShowLayer(newHandle, TransitionEntrySupport.Require(this, "toAssetId"), JsonSerializer.SerializeToElement(initial, TransitionEntrySupport.PlanJsonOptions), TransitionEntrySupport.Float(this, "toZ"), 1, TransitionEntrySupport.DisplayMode(this, "toDisplayMode", "Fill")), CrossFadeTransitionEntry.HideLayer(oldHandle, frames)]
        };
        return [TransitionEntrySupport.PlanEntry(context, plan)];
    }

    private static AnimationTrackDefinition Track(string handle, string property, float from, float to, int frames, AnimationBlendMode blendMode) => new()
    {
        HandleId = handle, Property = property, BlendMode = blendMode,
        Keys = [new AnimationKeyframeDefinition { Frame = 0, Value = from, InterpolationToNext = AnimationInterpolation.Linear }, new AnimationKeyframeDefinition { Frame = frames, Value = to }]
    };
}

/// <summary>
/// Reveals the incoming layer through a layer-attached blinds mask. The mask is a
/// normal animatable Effect instance, so the generated plan has no renderer-specific tracks.
/// </summary>
public sealed class BlindsTransitionEntry : NonPrimitiveEntry
{
    public const string TypeId = "transition.blinds";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("playbackHandleId", EntryParameterType.Text), ("oldHandleId", EntryParameterType.Text), ("newHandleId", EntryParameterType.Text), ("assetId", EntryParameterType.ImageAsset), ("transform", EntryParameterType.Json), ("z", EntryParameterType.Float), ("displayMode", EntryParameterType.Select), ("bladeCount", EntryParameterType.Integer), ("orientation", EntryParameterType.Select), ("frameRate", EntryParameterType.Integer), ("durationFrames", EntryParameterType.Integer), ("blocking", EntryParameterType.Select), ("skippable", EntryParameterType.Select), ("batchId", EntryParameterType.Text));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transform", "{}"), ("z", "0"), ("displayMode", "Fill"), ("bladeCount", "12"), ("orientation", "Vertical"), ("frameRate", "60"), ("durationFrames", "48"), ("blocking", "false"), ("skippable", "true"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("displayMode", ["Native", "Tile", "Fill", "Uniform", "UniformToFill"]), ("orientation", ["Vertical", "Horizontal"]), ("blocking", ["false", "true"]), ("skippable", ["false", "true"]));

    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context)
    {
        var playback = TransitionEntrySupport.Require(this, "playbackHandleId");
        var oldHandle = TransitionEntrySupport.Require(this, "oldHandleId");
        var newHandle = TransitionEntrySupport.Require(this, "newHandleId");
        var frames = TransitionEntrySupport.PositiveInt(this, "durationFrames");
        var bladeCount = TransitionEntrySupport.PositiveInt(this, "bladeCount");
        var orientation = TransitionEntrySupport.Get(this, "orientation", "Vertical");
        if (!string.Equals(orientation, "Vertical", StringComparison.OrdinalIgnoreCase) && !string.Equals(orientation, "Horizontal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Transition '{TypeId}' has an unknown orientation.");
        var mask = $"{context.SourceEntryId}:blinds-mask";
        var maskParameters = JsonSerializer.SerializeToElement(new { bladeCount, orientation }, TransitionEntrySupport.PlanJsonOptions);
        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = playback,
            FrameRate = TransitionEntrySupport.PositiveInt(this, "frameRate"),
            DurationFrames = frames,
            Blocking = TransitionEntrySupport.Bool(this, "blocking"),
            Skippable = TransitionEntrySupport.Bool(this, "skippable"),
            BatchId = TransitionEntrySupport.NullIfWhiteSpace(TransitionEntrySupport.Get(this, "batchId", "")),
            Tracks = [new AnimationTrackDefinition
            {
                HandleId = mask,
                Property = "progress",
                Keys = [new AnimationKeyframeDefinition { Frame = 0, Value = 0, InterpolationToNext = AnimationInterpolation.Linear }, new AnimationKeyframeDefinition { Frame = frames, Value = 1 }]
            }],
            Events =
            [
                CrossFadeTransitionEntry.ShowLayer(newHandle, TransitionEntrySupport.Require(this, "assetId"), TransitionEntrySupport.Json(this, "transform", "{}"), TransitionEntrySupport.Float(this, "z"), 1, TransitionEntrySupport.DisplayMode(this, "displayMode", "Fill")),
                ApplyMask(mask, newHandle, maskParameters),
                StopMask(mask, frames),
                CrossFadeTransitionEntry.HideLayer(oldHandle, frames)
            ]
        };
        return [TransitionEntrySupport.PlanEntry(context, plan)];
    }

    private static AnimationPlanEventDefinition ApplyMask(string instanceId, string targetHandleId, JsonElement parameters) => new()
    {
        Frame = 0,
        Type = ApplyEffectEntry.TypeId,
        Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["id"] = JsonSerializer.SerializeToElement("mask.blinds"),
            ["instanceId"] = JsonSerializer.SerializeToElement(instanceId),
            ["targetHandleId"] = JsonSerializer.SerializeToElement(targetHandleId),
            ["parameters"] = parameters
        }
    };

    private static AnimationPlanEventDefinition StopMask(string instanceId, int frame) => new()
    {
        Frame = frame,
        Type = StopEffectEntry.TypeId,
        Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instanceId"] = JsonSerializer.SerializeToElement(instanceId)
        }
    };
}

/// <summary>Base for color-field transitions that cover one outgoing Layer while replacing it with an incoming Layer.</summary>
public abstract class ColorFieldTransitionEntryBase : NonPrimitiveEntry
{
    private const int TimelineFrameRate = 60;
    protected static IReadOnlyDictionary<string, EntryParameterType> BaseParameterTypes { get; } = EntrySchema.Parameters(("playbackHandleId", EntryParameterType.Text), ("fromLayerHandleId", EntryParameterType.Text), ("toLayerHandleId", EntryParameterType.Text), ("toAssetId", EntryParameterType.ImageAsset), ("toTransform", EntryParameterType.Json), ("toZ", EntryParameterType.Float), ("toDisplayMode", EntryParameterType.Select), ("overlayZ", EntryParameterType.Float), ("fadeInDuration", EntryParameterType.Float), ("holdDuration", EntryParameterType.Float), ("fadeOutDuration", EntryParameterType.Float), ("blocking", EntryParameterType.Select), ("skippable", EntryParameterType.Select), ("batchId", EntryParameterType.Text));
    protected static IReadOnlyDictionary<string, string> BaseDefaultValues { get; } = EntrySchema.Defaults(("toTransform", "{}"), ("toZ", "0"), ("toDisplayMode", "Fill"), ("overlayZ", "1000"), ("fadeInDuration", "0.4"), ("holdDuration", "0.1"), ("fadeOutDuration", "0.4"), ("blocking", "false"), ("skippable", "true"));
    protected static IReadOnlyDictionary<string, IReadOnlyList<string>> BaseParameterOptions { get; } = EntrySchema.Options(("toDisplayMode", ["Native", "Tile", "Fill", "Uniform", "UniformToFill"]), ("blocking", ["false", "true"]), ("skippable", ["false", "true"]));
    protected abstract string OverlayColor { get; }

    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context)
    {
        var fadeIn = Frames("fadeInDuration", true); var hold = Frames("holdDuration", false); var fadeOut = Frames("fadeOutDuration", true);
        var swapFrame = fadeIn; var fadeOutStart = swapFrame + hold; var duration = fadeOutStart + fadeOut;
        var oldHandle = TransitionEntrySupport.Require(this, "fromLayerHandleId"); var newHandle = TransitionEntrySupport.Require(this, "toLayerHandleId");
        var overlay = $"{context.SourceEntryId}:color-overlay";
        if (!Layer.IsValidColor(OverlayColor)) throw new InvalidDataException($"Transition '{Type}' has an invalid overlay color '{OverlayColor}'.");
        var keys = new List<AnimationKeyframeDefinition> { new() { Frame = 0, Value = 0, InterpolationToNext = AnimationInterpolation.Linear }, new() { Frame = fadeIn, Value = 1, InterpolationToNext = AnimationInterpolation.Linear } };
        if (hold > 0) keys.Add(new AnimationKeyframeDefinition { Frame = fadeOutStart, Value = 1, InterpolationToNext = AnimationInterpolation.Linear });
        keys.Add(new AnimationKeyframeDefinition { Frame = duration, Value = 0 });
        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = TransitionEntrySupport.Require(this, "playbackHandleId"), FrameRate = TimelineFrameRate, DurationFrames = duration,
            Blocking = TransitionEntrySupport.Bool(this, "blocking"), Skippable = TransitionEntrySupport.Bool(this, "skippable"), BatchId = TransitionEntrySupport.NullIfWhiteSpace(TransitionEntrySupport.Get(this, "batchId", "")),
            Tracks = [new AnimationTrackDefinition { HandleId = overlay, Property = "opacity", Keys = keys }],
            Events = [ShowColor(overlay), CrossFadeTransitionEntry.HideLayer(oldHandle, swapFrame), ShowAt(newHandle, swapFrame), CrossFadeTransitionEntry.HideLayer(overlay, duration)]
        };
        return [TransitionEntrySupport.PlanEntry(context, plan)];
    }

    private AnimationPlanEventDefinition ShowColor(string handle) => new() { Frame = 0, Type = ShowColorLayerEntry.TypeId, Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["handleId"] = JsonSerializer.SerializeToElement(handle), ["color"] = JsonSerializer.SerializeToElement(OverlayColor), ["transform"] = JsonSerializer.SerializeToElement(new { }), ["z"] = JsonSerializer.SerializeToElement(TransitionEntrySupport.Float(this, "overlayZ")), ["opacity"] = JsonSerializer.SerializeToElement(0f) } };
    private AnimationPlanEventDefinition ShowAt(string handle, int frame) => new() { Frame = frame, Type = ShowLayerEntry.TypeId, Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["handleId"] = JsonSerializer.SerializeToElement(handle), ["assetId"] = JsonSerializer.SerializeToElement(TransitionEntrySupport.Require(this, "toAssetId")), ["transform"] = TransitionEntrySupport.Json(this, "toTransform", "{}"), ["z"] = JsonSerializer.SerializeToElement(TransitionEntrySupport.Float(this, "toZ")), ["opacity"] = JsonSerializer.SerializeToElement(1f), ["displayMode"] = JsonSerializer.SerializeToElement(TransitionEntrySupport.DisplayMode(this, "toDisplayMode", "Fill").ToString()) } };
    private int Frames(string name, bool positive)
    {
        var seconds = TransitionEntrySupport.Float(this, name);
        if (seconds < 0 || (positive && seconds <= 0)) throw new InvalidDataException($"Transition '{Type}' requires {(positive ? "a positive" : "a non-negative")} duration '{name}'.");
        return (int)Math.Round(seconds * TimelineFrameRate, MidpointRounding.AwayFromZero);
    }
}

public sealed class BlackFadeTransitionEntry : ColorFieldTransitionEntryBase { public const string TypeId = "transition.fadeBlack"; public override string Type => TypeId; protected override string OverlayColor => "#000000"; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => BaseParameterTypes; public static IReadOnlyDictionary<string, string> DefaultValues => BaseDefaultValues; public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => BaseParameterOptions; }
public sealed class WhiteFadeTransitionEntry : ColorFieldTransitionEntryBase { public const string TypeId = "transition.fadeWhite"; public override string Type => TypeId; protected override string OverlayColor => "#FFFFFF"; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => BaseParameterTypes; public static IReadOnlyDictionary<string, string> DefaultValues => BaseDefaultValues; public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => BaseParameterOptions; }
public sealed class ColorFadeTransitionEntry : ColorFieldTransitionEntryBase
{
    public const string TypeId = "transition.fadeColor"; public override string Type => TypeId; protected override string OverlayColor => TransitionEntrySupport.Require(this, "color");
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = BaseParameterTypes.Append(new KeyValuePair<string, EntryParameterType>("color", EntryParameterType.Text)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = BaseDefaultValues.Append(new KeyValuePair<string, string>("color", "#000000")).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => BaseParameterOptions;
}
