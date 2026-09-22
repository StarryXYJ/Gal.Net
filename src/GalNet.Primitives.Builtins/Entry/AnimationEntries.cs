using GalNet.Core.Scene;

namespace GalNet.Core.Entry;

/// <summary>Plays one interpolated property animation on an active scene instance.</summary>
public sealed class AnimateEntry : PrimitiveEntry
{
    public const string TypeId = "animate";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("playbackHandleId", EntryParameterType.Text), ("handleId", EntryParameterType.Text), ("property", EntryParameterType.Select), ("from", EntryParameterType.Float), ("to", EntryParameterType.Float), ("duration", EntryParameterType.Float), ("curve", EntryParameterType.Select), ("blocking", EntryParameterType.Select), ("skippable", EntryParameterType.Select), ("batchId", EntryParameterType.Text), ("loopMode", EntryParameterType.Select), ("blendMode", EntryParameterType.Select));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("duration", "0.25"), ("curve", "Linear"), ("blocking", "false"), ("skippable", "false"), ("loopMode", "Once"), ("blendMode", "Replace"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("property", Layer.AnimationProperties.Select(property => property.Name).ToArray()), ("curve", ["Linear", "Step", "EaseIn", "EaseOut", "EaseInOut"]), ("blocking", ["false", "true"]), ("skippable", ["false", "true"]), ("loopMode", ["Once", "Loop", "PingPong"]), ("blendMode", ["Replace", "Additive"]));
}

public sealed class PlayAnimationPlanEntry : PrimitiveEntry
{
    public const string TypeId = "animation.play";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("plan", EntryParameterType.Json));
}

public sealed class StopAnimationEntry : PrimitiveEntry
{
    public const string TypeId = "animation.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("playbackHandleId", EntryParameterType.Text), ("mode", EntryParameterType.Select));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("mode", "AfterIteration"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("mode", ["AfterIteration", "CompleteImmediately"]));
}
