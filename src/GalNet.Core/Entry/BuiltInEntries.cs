using GalNet.Core.Scene;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Core.Entry;

internal static class EntrySchema
{
    public static IReadOnlyDictionary<string, EntryParameterType> Parameters(params (string Name, EntryParameterType Type)[] items) =>
        new Dictionary<string, EntryParameterType>(items.ToDictionary(x => x.Name, x => x.Type), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Defaults(params (string Name, string Value)[] items) =>
        new Dictionary<string, string>(items.ToDictionary(x => x.Name, x => x.Value), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Options(params (string Name, string[] Values)[] items) =>
        new Dictionary<string, IReadOnlyList<string>>(items.ToDictionary(x => x.Name, x => (IReadOnlyList<string>)x.Values), StringComparer.Ordinal);
}

public sealed class TextEntry : PrimitiveEntry
{
    public const string TypeId = "text";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("speaker", EntryParameterType.Autocomplete), ("content", EntryParameterType.MultilineText),
        ("voice", EntryParameterType.AudioAsset));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults();
}

public sealed class ShowLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.show";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("handleId", EntryParameterType.Text), ("assetId", EntryParameterType.ImageAsset), ("transform", EntryParameterType.Json),
        ("z", EntryParameterType.Float), ("opacity", EntryParameterType.Float), ("displayMode", EntryParameterType.Select), ("transitionId", EntryParameterType.Text),
        ("transitionDuration", EntryParameterType.Float), ("transitionBlocking", EntryParameterType.Select), ("transitionParameters", EntryParameterType.MultilineText));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transform", "{}"), ("z", "0"), ("opacity", "1"), ("displayMode", "Native"), ("transitionDuration", "0.5"), ("transitionBlocking", "false"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("transitionBlocking", ["false", "true"]), ("displayMode", ["Native", "Tile", "Fill", "Uniform", "UniformToFill"]));
}

/// <summary>Shows a transient solid-color layer, primarily for compiled color-field transitions.</summary>
public sealed class ShowColorLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.showColor";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("handleId", EntryParameterType.Text), ("color", EntryParameterType.Text), ("transform", EntryParameterType.Json),
        ("z", EntryParameterType.Float), ("opacity", EntryParameterType.Float));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(
        ("transform", "{}"), ("z", "1000"), ("opacity", "1"));
}

public sealed class HideLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.hide";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("handleId", EntryParameterType.Text), ("transitionId", EntryParameterType.Text), ("transitionDuration", EntryParameterType.Float),
        ("transitionBlocking", EntryParameterType.Select), ("transitionParameters", EntryParameterType.MultilineText));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transitionDuration", "0.5"), ("transitionBlocking", "false"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = ShowLayerEntry.ParameterOptions;
}

public sealed class MoveLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.move";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("handleId", EntryParameterType.Text), ("transform", EntryParameterType.Json),
        ("z", EntryParameterType.Float), ("duration", EntryParameterType.Float));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transform", "{}"), ("z", "0"), ("duration", "0.5"));
}

public sealed class ReplaceLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.replace";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("handleId", EntryParameterType.Text), ("assetId", EntryParameterType.ImageAsset));
}

/// <summary>Plays one interpolated property animation on an active scene instance.</summary>
/// <remarks>
/// <c>playbackHandleId</c> identifies this playback and must be unique while active;
/// <c>handleId</c> and <c>property</c> identify the target. <c>from</c> is optional,
/// <c>to</c> is required, and <c>duration</c> is expressed in seconds. Looping animations
/// must be non-blocking and non-skippable; <c>batchId</c> groups eligible one-shot animations
/// for a single advance-to-skip operation.
/// </remarks>
public sealed class AnimateEntry : PrimitiveEntry
{
    public const string TypeId = "animate";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("playbackHandleId", EntryParameterType.Text), ("handleId", EntryParameterType.Text), ("property", EntryParameterType.Select), ("from", EntryParameterType.Float),
        ("to", EntryParameterType.Float), ("duration", EntryParameterType.Float), ("curve", EntryParameterType.Select),
        ("blocking", EntryParameterType.Select), ("skippable", EntryParameterType.Select), ("batchId", EntryParameterType.Text), ("loopMode", EntryParameterType.Select),
        ("blendMode", EntryParameterType.Select));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(
        ("duration", "0.25"), ("curve", "Linear"), ("blocking", "false"), ("skippable", "false"), ("loopMode", "Once"), ("blendMode", "Replace"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(
        ("property", Layer.AnimationProperties.Select(property => property.Name).ToArray()),
        ("curve", ["Linear", "Step", "EaseIn", "EaseOut", "EaseInOut"]),
        ("blocking", ["false", "true"]), ("skippable", ["false", "true"]), ("loopMode", ["Once", "Loop", "PingPong"]),
        ("blendMode", ["Replace", "Additive"]));
}

/// <summary>Plays a JSON keyframe timeline containing parallel property tracks and timed entry events.</summary>
/// <remarks>The <c>plan</c> parameter is an <see cref="AnimationPlanDefinition"/> serialized as JSON.</remarks>
public sealed class PlayAnimationPlanEntry : PrimitiveEntry
{
    public const string TypeId = "animation.play";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("plan", EntryParameterType.Json));
}

/// <summary>Non-primitive cross-fade that compiles into one keyframe animation plan.</summary>
public sealed class CrossFadeTransitionEntry : NonPrimitiveEntry
{
    private static readonly JsonSerializerOptions PlanJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public const string TypeId = "transition.crossFade";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("playbackHandleId", EntryParameterType.Text), ("oldHandleId", EntryParameterType.Text), ("newHandleId", EntryParameterType.Text),
        ("assetId", EntryParameterType.ImageAsset), ("transform", EntryParameterType.Json), ("z", EntryParameterType.Float),
        ("displayMode", EntryParameterType.Select), ("frameRate", EntryParameterType.Integer), ("durationFrames", EntryParameterType.Integer),
        ("blocking", EntryParameterType.Select), ("skippable", EntryParameterType.Select), ("batchId", EntryParameterType.Text));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(
        ("transform", "{}"), ("z", "0"), ("displayMode", "Fill"), ("frameRate", "60"), ("durationFrames", "48"),
        ("blocking", "false"), ("skippable", "true"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(
        ("displayMode", ["Native", "Tile", "Fill", "Uniform", "UniformToFill"]),
        ("blocking", ["false", "true"]), ("skippable", ["false", "true"]));

    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context)
    {
        var playbackHandleId = Require("playbackHandleId");
        var oldHandleId = Require("oldHandleId");
        var newHandleId = Require("newHandleId");
        var assetId = Require("assetId");
        var frameRate = ReadPositiveInt("frameRate");
        var durationFrames = ReadPositiveInt("durationFrames");
        var displayMode = Get("displayMode", "Fill");
        if (!Enum.TryParse<LayerDisplayMode>(displayMode, true, out var parsedDisplayMode) || !Enum.IsDefined(parsedDisplayMode))
            throw new InvalidDataException($"Unknown layer displayMode '{displayMode}'.");

        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = playbackHandleId,
            FrameRate = frameRate,
            DurationFrames = durationFrames,
            Blocking = ReadBool("blocking"),
            Skippable = ReadBool("skippable"),
            BatchId = NullIfWhiteSpace(Get("batchId", "")),
            Tracks =
            [
                Track(oldHandleId, 1, 0, durationFrames),
                Track(newHandleId, 0, 1, durationFrames)
            ],
            Events =
            [
                new AnimationPlanEventDefinition
                {
                    Frame = 0,
                    Type = ShowLayerEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["handleId"] = JsonSerializer.SerializeToElement(newHandleId),
                        ["assetId"] = JsonSerializer.SerializeToElement(assetId),
                        ["transform"] = ReadJson("transform", "{}"),
                        ["z"] = JsonSerializer.SerializeToElement(ReadFloat("z")),
                        ["opacity"] = JsonSerializer.SerializeToElement(0f),
                        ["displayMode"] = JsonSerializer.SerializeToElement(parsedDisplayMode.ToString())
                    }
                },
                new AnimationPlanEventDefinition
                {
                    Frame = durationFrames,
                    Type = HideLayerEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["handleId"] = JsonSerializer.SerializeToElement(oldHandleId)
                    }
                }
            ]
        };

        var primitive = context.CreatePrimitive<PlayAnimationPlanEntry>();
        primitive.Values["plan"] = JsonSerializer.Serialize(plan, PlanJsonOptions);
        return [primitive];
    }

    private AnimationTrackDefinition Track(string handleId, float from, float to, int durationFrames) => new()
    {
        HandleId = handleId,
        Property = "opacity",
        Keys =
        [
            new AnimationKeyframeDefinition { Frame = 0, Value = from, InterpolationToNext = AnimationInterpolation.Linear },
            new AnimationKeyframeDefinition { Frame = durationFrames, Value = to }
        ]
    };

    private string Get(string name, string fallback) => Values.TryGetValue(name, out var value) ? value : fallback;

    private string Require(string name)
    {
        var value = Get(name, "");
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"Transition '{TypeId}' requires '{name}'.");
    }

    private int ReadPositiveInt(string name) => int.TryParse(Get(name, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
        ? value
        : throw new InvalidDataException($"Transition '{TypeId}' requires a positive integer '{name}'.");

    private float ReadFloat(string name) => float.TryParse(Get(name, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        ? value
        : throw new InvalidDataException($"Transition '{TypeId}' requires a numeric '{name}'.");

    private bool ReadBool(string name) => bool.TryParse(Get(name, "false"), out var value)
        ? value
        : throw new InvalidDataException($"Transition '{TypeId}' requires a boolean '{name}'.");

    private JsonElement ReadJson(string name, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(Get(name, fallback));
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Transition '{TypeId}' has invalid JSON '{name}'.", exception);
        }
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Base for color-field transitions that cover one outgoing Layer while replacing it with an incoming Layer.</summary>
public abstract class ColorFieldTransitionEntryBase : NonPrimitiveEntry
{
    private const int TimelineFrameRate = 60;
    private static readonly JsonSerializerOptions PlanJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected static IReadOnlyDictionary<string, EntryParameterType> BaseParameterTypes { get; } = EntrySchema.Parameters(
        ("playbackHandleId", EntryParameterType.Text), ("fromLayerHandleId", EntryParameterType.Text), ("toLayerHandleId", EntryParameterType.Text),
        ("toAssetId", EntryParameterType.ImageAsset), ("toTransform", EntryParameterType.Json), ("toZ", EntryParameterType.Float),
        ("toDisplayMode", EntryParameterType.Select), ("overlayZ", EntryParameterType.Float),
        ("fadeInDuration", EntryParameterType.Float), ("holdDuration", EntryParameterType.Float), ("fadeOutDuration", EntryParameterType.Float),
        ("blocking", EntryParameterType.Select), ("skippable", EntryParameterType.Select),
        ("batchId", EntryParameterType.Text));
    protected static IReadOnlyDictionary<string, string> BaseDefaultValues { get; } = EntrySchema.Defaults(
        ("toTransform", "{}"), ("toZ", "0"), ("toDisplayMode", "Fill"), ("overlayZ", "1000"),
        ("fadeInDuration", "0.4"), ("holdDuration", "0.1"), ("fadeOutDuration", "0.4"), ("blocking", "false"), ("skippable", "true"));
    protected static IReadOnlyDictionary<string, IReadOnlyList<string>> BaseParameterOptions { get; } = EntrySchema.Options(
        ("toDisplayMode", ["Native", "Tile", "Fill", "Uniform", "UniformToFill"]),
        ("blocking", ["false", "true"]), ("skippable", ["false", "true"]));

    /// <summary>Hexadecimal color to render in the transient overlay.</summary>
    protected abstract string OverlayColor { get; }

    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context)
    {
        var playbackHandleId = Require("playbackHandleId");
        var fromLayerHandleId = Require("fromLayerHandleId");
        var toLayerHandleId = Require("toLayerHandleId");
        var toAssetId = Require("toAssetId");
        var fadeInFrames = ToPositiveFrames("fadeInDuration");
        var holdFrames = ToNonNegativeFrames("holdDuration");
        var fadeOutFrames = ToPositiveFrames("fadeOutDuration");
        var swapFrame = fadeInFrames;
        var fadeOutStartFrame = swapFrame + holdFrames;
        var durationFrames = fadeOutStartFrame + fadeOutFrames;
        var displayMode = Get("toDisplayMode", "Fill");
        if (!Enum.TryParse<LayerDisplayMode>(displayMode, true, out var parsedDisplayMode) || !Enum.IsDefined(parsedDisplayMode))
            throw new InvalidDataException($"Unknown layer displayMode '{displayMode}'.");
        if (!Layer.IsValidColor(OverlayColor)) throw new InvalidDataException($"Transition '{Type}' has an invalid overlay color '{OverlayColor}'.");

        var overlayHandleId = $"{context.SourceEntryId}:color-overlay";
        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = playbackHandleId,
            FrameRate = TimelineFrameRate,
            DurationFrames = durationFrames,
            Blocking = ReadBool("blocking"),
            Skippable = ReadBool("skippable"),
            BatchId = NullIfWhiteSpace(Get("batchId", "")),
            Tracks =
            [
                new AnimationTrackDefinition
                {
                    HandleId = overlayHandleId,
                    Property = "opacity",
                    Keys = CreateOverlayOpacityKeys(swapFrame, fadeOutStartFrame, durationFrames)
                }
            ],
            Events =
            [
                new AnimationPlanEventDefinition
                {
                    Frame = 0,
                    Type = ShowColorLayerEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["handleId"] = JsonSerializer.SerializeToElement(overlayHandleId),
                        ["color"] = JsonSerializer.SerializeToElement(OverlayColor),
                        ["transform"] = JsonSerializer.SerializeToElement(new { }),
                        ["z"] = JsonSerializer.SerializeToElement(ReadFloat("overlayZ")),
                        ["opacity"] = JsonSerializer.SerializeToElement(0f)
                    }
                },
                new AnimationPlanEventDefinition
                {
                    Frame = swapFrame,
                    Type = HideLayerEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["handleId"] = JsonSerializer.SerializeToElement(fromLayerHandleId)
                    }
                },
                new AnimationPlanEventDefinition
                {
                    Frame = swapFrame,
                    Type = ShowLayerEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["handleId"] = JsonSerializer.SerializeToElement(toLayerHandleId),
                        ["assetId"] = JsonSerializer.SerializeToElement(toAssetId),
                        ["transform"] = ReadJson("toTransform", "{}"),
                        ["z"] = JsonSerializer.SerializeToElement(ReadFloat("toZ")),
                        ["opacity"] = JsonSerializer.SerializeToElement(1f),
                        ["displayMode"] = JsonSerializer.SerializeToElement(parsedDisplayMode.ToString())
                    }
                },
                new AnimationPlanEventDefinition
                {
                    Frame = durationFrames,
                    Type = HideLayerEntry.TypeId,
                    Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["handleId"] = JsonSerializer.SerializeToElement(overlayHandleId)
                    }
                }
            ]
        };

        var primitive = context.CreatePrimitive<PlayAnimationPlanEntry>();
        primitive.Values["plan"] = JsonSerializer.Serialize(plan, PlanJsonOptions);
        return [primitive];
    }

    protected string Get(string name, string fallback) => Values.TryGetValue(name, out var value) ? value : fallback;

    protected string Require(string name)
    {
        var value = Get(name, "");
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"Transition '{Type}' requires '{name}'.");
    }

    private List<AnimationKeyframeDefinition> CreateOverlayOpacityKeys(int fadeInFrames, int fadeOutStartFrame, int durationFrames)
    {
        var keys = new List<AnimationKeyframeDefinition>
        {
            new() { Frame = 0, Value = 0, InterpolationToNext = AnimationInterpolation.Linear },
            new() { Frame = fadeInFrames, Value = 1, InterpolationToNext = AnimationInterpolation.Linear }
        };
        if (fadeOutStartFrame > fadeInFrames)
            keys.Add(new AnimationKeyframeDefinition { Frame = fadeOutStartFrame, Value = 1, InterpolationToNext = AnimationInterpolation.Linear });
        keys.Add(new AnimationKeyframeDefinition { Frame = durationFrames, Value = 0 });
        return keys;
    }

    private int ToPositiveFrames(string name)
    {
        var duration = ReadNonNegativeDuration(name);
        if (duration <= 0) throw new InvalidDataException($"Transition '{Type}' requires a positive duration '{name}'.");
        return Math.Max(1, (int)Math.Round(duration * TimelineFrameRate, MidpointRounding.AwayFromZero));
    }

    private int ToNonNegativeFrames(string name) =>
        (int)Math.Round(ReadNonNegativeDuration(name) * TimelineFrameRate, MidpointRounding.AwayFromZero);

    private float ReadNonNegativeDuration(string name) => float.TryParse(Get(name, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0 && float.IsFinite(value)
        ? value
        : throw new InvalidDataException($"Transition '{Type}' requires a non-negative duration '{name}'.");

    protected float ReadFloat(string name) => float.TryParse(Get(name, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        ? value
        : throw new InvalidDataException($"Transition '{Type}' requires a numeric '{name}'.");

    protected bool ReadBool(string name) => bool.TryParse(Get(name, "false"), out var value)
        ? value
        : throw new InvalidDataException($"Transition '{Type}' requires a boolean '{name}'.");

    protected JsonElement ReadJson(string name, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(Get(name, fallback));
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Transition '{Type}' has invalid JSON '{name}'.", exception);
        }
    }

    protected static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Fades a Layer through an opaque black field before showing its replacement Layer.</summary>
public sealed class BlackFadeTransitionEntry : ColorFieldTransitionEntryBase
{
    public const string TypeId = "transition.fadeBlack";
    public override string Type => TypeId;
    protected override string OverlayColor => "#000000";
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => BaseParameterTypes;
    public static IReadOnlyDictionary<string, string> DefaultValues => BaseDefaultValues;
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => BaseParameterOptions;
}

/// <summary>Fades a Layer through an opaque white field before showing its replacement Layer.</summary>
public sealed class WhiteFadeTransitionEntry : ColorFieldTransitionEntryBase
{
    public const string TypeId = "transition.fadeWhite";
    public override string Type => TypeId;
    protected override string OverlayColor => "#FFFFFF";
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => BaseParameterTypes;
    public static IReadOnlyDictionary<string, string> DefaultValues => BaseDefaultValues;
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => BaseParameterOptions;
}

/// <summary>Fades a Layer through an authored hexadecimal color field before showing its replacement Layer.</summary>
public sealed class ColorFadeTransitionEntry : ColorFieldTransitionEntryBase
{
    public const string TypeId = "transition.fadeColor";
    public override string Type => TypeId;
    protected override string OverlayColor => Require("color");
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } =
        BaseParameterTypes.Concat(new[] { new KeyValuePair<string, EntryParameterType>("color", EntryParameterType.Text) })
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } =
        BaseDefaultValues.Concat(new[] { new KeyValuePair<string, string>("color", "#000000") })
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => BaseParameterOptions;
}

/// <summary>Requests that a looped animation playback stop.</summary>
/// <remarks>
/// <c>playbackHandleId</c> selects the active playback. <c>mode</c> is <c>AfterIteration</c>
/// by default, or <c>CompleteImmediately</c> to finish without waiting for the current loop.
/// </remarks>
public sealed class StopAnimationEntry : PrimitiveEntry
{
    public const string TypeId = "animation.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("playbackHandleId", EntryParameterType.Text), ("mode", EntryParameterType.Select));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("mode", "AfterIteration"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(
        ("mode", ["AfterIteration", "CompleteImmediately"]));
}

public sealed class PlayAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.play";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("channel", EntryParameterType.Select), ("asset", EntryParameterType.AudioAsset), ("volume", EntryParameterType.Float),
        ("mode", EntryParameterType.Select), ("times", EntryParameterType.Integer));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("channel", "bgm"), ("volume", "0.8"), ("mode", "once"), ("times", "1"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("channel", ["bgm", "sfx", "voice"]), ("mode", ["once", "loop"]));
}

public sealed class StopAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("channel", EntryParameterType.Select));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("channel", "bgm"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("channel", ["bgm", "sfx", "voice"]));
}

public sealed class PauseAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.pause";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => StopAudioEntry.ParameterTypes;
    public static IReadOnlyDictionary<string, string> DefaultValues => StopAudioEntry.DefaultValues;
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => StopAudioEntry.ParameterOptions;
}

public sealed class ResumeAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.resume";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => StopAudioEntry.ParameterTypes;
    public static IReadOnlyDictionary<string, string> DefaultValues => StopAudioEntry.DefaultValues;
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => StopAudioEntry.ParameterOptions;
}

public sealed class EnqueueAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.enqueue";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("channel", EntryParameterType.Select), ("asset", EntryParameterType.AudioAsset), ("times", EntryParameterType.Integer));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("channel", "bgm"), ("times", "1"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => StopAudioEntry.ParameterOptions;
}

public sealed class PlayVideoEntry : PrimitiveEntry
{
    public const string TypeId = "video.play";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("asset", EntryParameterType.VideoAsset));
}

public sealed class StopVideoEntry : PrimitiveEntry
{
    public const string TypeId = "video.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters();
}

public sealed class ShowDialogueEntry : PrimitiveEntry
{
    public const string TypeId = "dialogue.show";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters();
}

public sealed class HideDialogueEntry : PrimitiveEntry
{
    public const string TypeId = "dialogue.hide";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => ShowDialogueEntry.ParameterTypes;
}

public sealed class ApplyEffectEntry : PrimitiveEntry
{
    public const string TypeId = "effect.apply";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("id", EntryParameterType.Text), ("instanceId", EntryParameterType.Text), ("duration", EntryParameterType.Float),
        ("blocking", EntryParameterType.Select), ("parameters", EntryParameterType.MultilineText));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("duration", "-1"), ("blocking", "false"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("blocking", ["false", "true"]));
}

public sealed class StopEffectEntry : PrimitiveEntry
{
    public const string TypeId = "effect.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("instanceId", EntryParameterType.Text));
}

public sealed class WaitEntry : PrimitiveEntry
{
    public const string TypeId = "wait";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("duration", EntryParameterType.Float));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("duration", "1"));
}

public sealed class SetVariableEntry : PrimitiveEntry
{
    public const string TypeId = "variable.set";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("target", EntryParameterType.VariableName), ("expression", EntryParameterType.Expression));
}

public sealed class UnlockGalleryEntry : PrimitiveEntry
{
    public const string TypeId = "unlock_gallery";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("category", EntryParameterType.Select), ("id", EntryParameterType.Integer));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("category", ["Portrait", "Cg", "Scene"]));
}
