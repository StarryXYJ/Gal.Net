using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Contracts.Builtins.Scene;

namespace GalNet.Contracts.Builtins;

internal static class TransitionEntrySupport
{
    public static readonly JsonSerializerOptions TimelineJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions TransformJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Require(string? value, string name, string type) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"Transition '{type}' requires '{name}'.");
    public static int PositiveInt(int value, string name, string type) => value > 0
        ? value : throw new InvalidDataException($"Transition '{type}' requires a positive integer '{name}'.");
    public static JsonElement Json(JsonElement value, string name, string type)
    {
        if (value.ValueKind != JsonValueKind.String) return value.Clone();
        try
        {
            using var document = JsonDocument.Parse(value.GetString()!);
            return document.RootElement.Clone();
        }
        catch (JsonException exception) { throw new InvalidDataException($"Transition '{type}' has invalid JSON '{name}'.", exception); }
    }
    public static LayerDisplayMode DisplayMode(LayerDisplayMode? value) =>
        value ?? throw new InvalidDataException("Unknown layer displayMode ''.");
    public static LayerTransform Transform(JsonElement value, string name, string type)
    {
        try
        {
            var transform = value.ValueKind == JsonValueKind.String
                ? JsonSerializer.Deserialize<LayerTransform>(value.GetString()!, TransformJsonOptions)
                : value.Deserialize<LayerTransform>(TransformJsonOptions);
            return transform is { ScaleX: > 0, ScaleY: > 0 } ? transform : throw new InvalidDataException();
        }
        catch (JsonException exception) { throw new InvalidDataException($"Transition '{type}' has invalid transform '{name}'.", exception); }
        catch (InvalidDataException) { throw new InvalidDataException($"Transition '{type}' requires positive scale values in '{name}'."); }
    }
    public static PrimitiveEntry TimelineEntry(EntryCompileContext context, TimelineDefinition timeline, string? batchId)
    {
        return new PrimitiveEntry(
            BuiltinEntryIds.PlayTimeline,
            ValueCodec.SerializeFields(new TimelineOptions { Timeline = JsonSerializer.SerializeToElement(timeline, TimelineJsonOptions) }, omitNullValues: true))
        {
            Condition = context.Condition,
            BatchId = batchId
        };
    }
    public static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Non-primitive cross-fade that compiles into one keyframe animation timeline.</summary>
internal static class CrossFadeTransition
{
    public static IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context, CrossFadeOptions options)
    {
        var playback = TransitionEntrySupport.Require(options.PlaybackHandleId, "playbackHandleId", BuiltinEntryIds.CrossFadeTransition);
        var oldHandle = TransitionEntrySupport.Require(options.OldHandleId, "oldHandleId", BuiltinEntryIds.CrossFadeTransition);
        var newHandle = TransitionEntrySupport.Require(options.NewHandleId, "newHandleId", BuiltinEntryIds.CrossFadeTransition);
        var asset = TransitionEntrySupport.Require(options.AssetId, "assetId", BuiltinEntryIds.CrossFadeTransition);
        var frames = TransitionEntrySupport.PositiveInt(options.DurationFrames, "durationFrames", BuiltinEntryIds.CrossFadeTransition);
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = playback,
            FrameRate = TransitionEntrySupport.PositiveInt(options.FrameRate, "frameRate", BuiltinEntryIds.CrossFadeTransition),
            DurationFrames = frames,
            Blocking = true,
            Skippable = true,
            Tracks = [OpacityTrack(oldHandle, 1, 0, frames), OpacityTrack(newHandle, 0, 1, frames)],
            Events = [ShowLayer(newHandle, asset, TransitionEntrySupport.Json(options.Transform, "transform", BuiltinEntryIds.CrossFadeTransition), options.Z, 0, TransitionEntrySupport.DisplayMode(options.DisplayMode)), HideLayer(oldHandle, frames)]
        };
        return [TransitionEntrySupport.TimelineEntry(context, timeline, TransitionEntrySupport.NullIfWhiteSpace(options.BatchId))];
    }

    internal static TimelineTrackDefinition OpacityTrack(string handle, float from, float to, int frames) => new()
    {
        HandleId = handle,
        Property = "opacity",
        Keys = [new TimelineKeyframeDefinition { Frame = 0, Value = from, InterpolationToNext = AnimationInterpolation.Linear }, new TimelineKeyframeDefinition { Frame = frames, Value = to }]
    };
    internal static TimelineEventDefinition ShowLayer(string handle, string asset, JsonElement transform, float z, float opacity, LayerDisplayMode displayMode) => new()
    {
        Frame = 0,
        Type = BuiltinEntryIds.ShowLayer,
        Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["handleId"] = JsonSerializer.SerializeToElement(handle), ["assetId"] = JsonSerializer.SerializeToElement(asset), ["transform"] = transform, ["z"] = JsonSerializer.SerializeToElement(z), ["opacity"] = JsonSerializer.SerializeToElement(opacity), ["displayMode"] = JsonSerializer.SerializeToElement(displayMode.ToString()) }
    };
    internal static TimelineEventDefinition HideLayer(string handle, int frame) => new()
    {
        Frame = frame,
        Type = BuiltinEntryIds.HideLayer,
        Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["handleId"] = JsonSerializer.SerializeToElement(handle) }
    };
}

/// <summary>Slides in the replacement layer and moves the outgoing layer by the same distance.</summary>
internal static class SlideTransition
{
    public static IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context, SlideOptions options)
    {
        var playback = TransitionEntrySupport.Require(options.PlaybackHandleId, "playbackHandleId", BuiltinEntryIds.SlideTransition);
        var oldHandle = TransitionEntrySupport.Require(options.FromLayerHandleId, "fromLayerHandleId", BuiltinEntryIds.SlideTransition);
        var newHandle = TransitionEntrySupport.Require(options.ToLayerHandleId, "toLayerHandleId", BuiltinEntryIds.SlideTransition);
        var destination = TransitionEntrySupport.Transform(options.ToTransform, "toTransform", BuiltinEntryIds.SlideTransition);
        var distance = options.Distance;
        if (distance <= 0) throw new InvalidDataException($"Transition '{BuiltinEntryIds.SlideTransition}' requires a positive 'distance'.");
        var direction = options.Direction;
        if (direction is null)
            throw new InvalidDataException($"Transition '{BuiltinEntryIds.SlideTransition}' has an unknown direction.");
        var (incomingX, incomingY) = direction switch { SlideDirection.Left => (distance, 0f), SlideDirection.Right => (-distance, 0f), SlideDirection.Up => (0f, distance), SlideDirection.Down => (0f, -distance), _ => (0f, 0f) };
        var initial = destination.Clone(); initial.X += incomingX; initial.Y += incomingY;
        var frames = TransitionEntrySupport.PositiveInt(options.DurationFrames, "durationFrames", BuiltinEntryIds.SlideTransition);
        var property = incomingX != 0 ? "transform.x" : "transform.y";
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = playback,
            FrameRate = TransitionEntrySupport.PositiveInt(options.FrameRate, "frameRate", BuiltinEntryIds.SlideTransition),
            DurationFrames = frames,
            Blocking = true,
            Skippable = true,
            Tracks = [Track(oldHandle, property, 0, -incomingX - incomingY, frames, AnimationBlendMode.Additive), Track(newHandle, property, incomingX != 0 ? initial.X : initial.Y, incomingX != 0 ? destination.X : destination.Y, frames, AnimationBlendMode.Replace)],
            Events = [CrossFadeTransition.ShowLayer(newHandle, TransitionEntrySupport.Require(options.ToAssetId, "toAssetId", BuiltinEntryIds.SlideTransition), JsonSerializer.SerializeToElement(initial, TransitionEntrySupport.TimelineJsonOptions), options.ToZ, 1, TransitionEntrySupport.DisplayMode(options.ToDisplayMode)), CrossFadeTransition.HideLayer(oldHandle, frames)]
        };
        return [TransitionEntrySupport.TimelineEntry(context, timeline, TransitionEntrySupport.NullIfWhiteSpace(options.BatchId))];
    }

    private static TimelineTrackDefinition Track(string handle, string property, float from, float to, int frames, AnimationBlendMode blendMode) => new()
    {
        HandleId = handle,
        Property = property,
        BlendMode = blendMode,
        Keys = [new TimelineKeyframeDefinition { Frame = 0, Value = from, InterpolationToNext = AnimationInterpolation.Linear }, new TimelineKeyframeDefinition { Frame = frames, Value = to }]
    };
}

/// <summary>
/// Reveals the incoming layer through a layer-attached blinds mask. The mask is a
/// normal animatable Effect instance, so the generated timeline has no renderer-specific tracks.
/// </summary>
internal static class BlindsTransition
{
    public static IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context, BlindsOptions options)
    {
        var playback = TransitionEntrySupport.Require(options.PlaybackHandleId, "playbackHandleId", BuiltinEntryIds.BlindsTransition);
        var oldHandle = TransitionEntrySupport.Require(options.OldHandleId, "oldHandleId", BuiltinEntryIds.BlindsTransition);
        var newHandle = TransitionEntrySupport.Require(options.NewHandleId, "newHandleId", BuiltinEntryIds.BlindsTransition);
        var maskProgram = TransitionEntrySupport.Require(options.MaskProgram, "maskProgram", BuiltinEntryIds.BlindsTransition);
        var frames = TransitionEntrySupport.PositiveInt(options.DurationFrames, "durationFrames", BuiltinEntryIds.BlindsTransition);
        var bladeCount = TransitionEntrySupport.PositiveInt(options.BladeCount, "bladeCount", BuiltinEntryIds.BlindsTransition);
        var orientation = options.Orientation?.ToString() ?? "";
        if (!string.Equals(orientation, "Vertical", StringComparison.OrdinalIgnoreCase) && !string.Equals(orientation, "Horizontal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Transition '{BuiltinEntryIds.BlindsTransition}' has an unknown orientation.");
        var mask = $"{context.SourceEntryId}:blinds-mask";
        var maskParameters = JsonSerializer.SerializeToElement(new { bladeCount, orientation }, TransitionEntrySupport.TimelineJsonOptions);
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = playback,
            FrameRate = TransitionEntrySupport.PositiveInt(options.FrameRate, "frameRate", BuiltinEntryIds.BlindsTransition),
            DurationFrames = frames,
            Blocking = true,
            Skippable = true,
            Tracks = [new TimelineTrackDefinition
            {
                HandleId = mask,
                Property = "progress",
                Keys = [new TimelineKeyframeDefinition { Frame = 0, Value = 0, InterpolationToNext = AnimationInterpolation.Linear }, new TimelineKeyframeDefinition { Frame = frames, Value = 1 }]
            }],
            Events =
            [
                ApplyMask(mask, newHandle, maskProgram, maskParameters),
                // A program effect is created asynchronously by presentation hosts.  Attach the
                // closed mask before queuing the incoming layer, otherwise the compositor can
                // draw one unmasked frame while the program factory is initialized.
                CrossFadeTransition.ShowLayer(newHandle, TransitionEntrySupport.Require(options.AssetId, "assetId", BuiltinEntryIds.BlindsTransition), TransitionEntrySupport.Json(options.Transform, "transform", BuiltinEntryIds.BlindsTransition), options.Z, 1, TransitionEntrySupport.DisplayMode(options.DisplayMode)),
                StopMask(mask, frames),
                CrossFadeTransition.HideLayer(oldHandle, frames)
            ]
        };
        return [TransitionEntrySupport.TimelineEntry(context, timeline, TransitionEntrySupport.NullIfWhiteSpace(options.BatchId))];
    }

    private static TimelineEventDefinition ApplyMask(string instanceId, string targetHandleId, string program, JsonElement parameters) => new()
    {
        Frame = 0,
        Type = BuiltinEntryIds.ApplyEffect,
        Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["program"] = JsonSerializer.SerializeToElement(program),
            ["instanceId"] = JsonSerializer.SerializeToElement(instanceId),
            ["targetHandleId"] = JsonSerializer.SerializeToElement(targetHandleId),
            ["parameters"] = parameters
        }
    };

    private static TimelineEventDefinition StopMask(string instanceId, int frame) => new()
    {
        Frame = frame,
        Type = BuiltinEntryIds.StopEffect,
        Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instanceId"] = JsonSerializer.SerializeToElement(instanceId)
        }
    };
}

/// <summary>Shared expansion for preset and authored color-field transitions.</summary>
internal static class ColorFieldTransition
{
    private const int TimelineFrameRate = 60;
    public static IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context, ColorFieldOptionsBase options, string? color, string type)
    {
        var fadeIn = Frames(options.FadeInDuration, "fadeInDuration", true, type);
        var hold = Frames(options.HoldDuration, "holdDuration", false, type);
        var fadeOut = Frames(options.FadeOutDuration, "fadeOutDuration", true, type);
        var swapFrame = fadeIn; var fadeOutStart = swapFrame + hold; var duration = fadeOutStart + fadeOut;
        var oldHandle = TransitionEntrySupport.Require(options.FromLayerHandleId, "fromLayerHandleId", type);
        var newHandle = TransitionEntrySupport.Require(options.ToLayerHandleId, "toLayerHandleId", type);
        var overlay = $"{context.SourceEntryId}:color-overlay";
        if (!Layer.IsValidColor(color ?? "")) throw new InvalidDataException($"Transition '{type}' has an invalid overlay color '{color}'.");
        var keys = new List<TimelineKeyframeDefinition> { new() { Frame = 0, Value = 0, InterpolationToNext = AnimationInterpolation.Linear }, new() { Frame = fadeIn, Value = 1, InterpolationToNext = AnimationInterpolation.Linear } };
        if (hold > 0) keys.Add(new TimelineKeyframeDefinition { Frame = fadeOutStart, Value = 1, InterpolationToNext = AnimationInterpolation.Linear });
        keys.Add(new TimelineKeyframeDefinition { Frame = duration, Value = 0 });
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = TransitionEntrySupport.Require(options.PlaybackHandleId, "playbackHandleId", type),
            FrameRate = TimelineFrameRate,
            DurationFrames = duration,
            Blocking = true,
            Skippable = true,
            Tracks = [new TimelineTrackDefinition { HandleId = overlay, Property = "opacity", Keys = keys }],
            Events = [new() { Frame = 0, Type = BuiltinEntryIds.ShowColorLayer, Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            { ["handleId"] = JsonSerializer.SerializeToElement(overlay), ["color"] = JsonSerializer.SerializeToElement(color), ["transform"] = JsonSerializer.SerializeToElement(new { }), ["z"] = JsonSerializer.SerializeToElement(options.OverlayZ), ["opacity"] = JsonSerializer.SerializeToElement(0f) } },
            CrossFadeTransition.HideLayer(oldHandle, swapFrame),
            new() { Frame = swapFrame, Type = BuiltinEntryIds.ShowLayer, Parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            { ["handleId"] = JsonSerializer.SerializeToElement(newHandle), ["assetId"] = JsonSerializer.SerializeToElement(TransitionEntrySupport.Require(options.ToAssetId, "toAssetId", type)), ["transform"] = TransitionEntrySupport.Json(options.ToTransform, "toTransform", type), ["z"] = JsonSerializer.SerializeToElement(options.ToZ), ["opacity"] = JsonSerializer.SerializeToElement(1f), ["displayMode"] = JsonSerializer.SerializeToElement(TransitionEntrySupport.DisplayMode(options.ToDisplayMode).ToString()) } },
            CrossFadeTransition.HideLayer(overlay, duration)]
        };
        return [TransitionEntrySupport.TimelineEntry(context, timeline, TransitionEntrySupport.NullIfWhiteSpace(options.BatchId))];
    }
    private static int Frames(float seconds, string name, bool positive, string type)
    {
        if (seconds < 0 || (positive && seconds <= 0)) throw new InvalidDataException($"Transition '{type}' requires {(positive ? "a positive" : "a non-negative")} duration '{name}'.");
        return (int)Math.Round(seconds * TimelineFrameRate, MidpointRounding.AwayFromZero);
    }
}
