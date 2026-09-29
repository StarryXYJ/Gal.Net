using GalNet.Core.Entry;

namespace GalNet.Primitives.Builtins;

public sealed class PlayAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.play";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("trackId", EntryParameterType.Text), ("asset", EntryParameterType.AudioAsset), ("gain", EntryParameterType.Float), ("placement", EntryParameterType.Select), ("fadeIn", EntryParameterType.Float));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("trackId", "bgm"), ("gain", "1"), ("placement", "enqueue"), ("fadeIn", "0"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("placement", ["enqueue", "next", "replace", "replaceAll", "replaceIfDifferent"]));
}

public sealed class StopAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("trackId", EntryParameterType.Text), ("clearQueue", EntryParameterType.Select), ("fadeOut", EntryParameterType.Float));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("trackId", "bgm"), ("clearQueue", "true"), ("fadeOut", "0"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("clearQueue", ["true", "false"]));
}

public sealed class PauseAudioEntry : PrimitiveEntry { public const string TypeId = "audio.pause"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("trackId", EntryParameterType.Text)); public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("trackId", "bgm")); }
public sealed class ResumeAudioEntry : PrimitiveEntry { public const string TypeId = "audio.resume"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => PauseAudioEntry.ParameterTypes; public static IReadOnlyDictionary<string, string> DefaultValues => PauseAudioEntry.DefaultValues; }
public sealed class NextAudioEntry : PrimitiveEntry { public const string TypeId = "audio.next"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => PauseAudioEntry.ParameterTypes; public static IReadOnlyDictionary<string, string> DefaultValues => PauseAudioEntry.DefaultValues; }
public sealed class ClearAudioEntry : PrimitiveEntry { public const string TypeId = "audio.clear"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => PauseAudioEntry.ParameterTypes; public static IReadOnlyDictionary<string, string> DefaultValues => PauseAudioEntry.DefaultValues; }

public sealed class EnqueueAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.enqueue";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => PlayAudioEntry.ParameterTypes;
    public static IReadOnlyDictionary<string, string> DefaultValues => PlayAudioEntry.DefaultValues;
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => PlayAudioEntry.ParameterOptions;
}

public sealed class ApplyAudioSEffectEntry : PrimitiveEntry
{
    public const string TypeId = "audio.seffect.apply";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("trackId", EntryParameterType.Text), ("instanceId", EntryParameterType.Text), ("id", EntryParameterType.Text), ("parameters", EntryParameterType.Json));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("trackId", "bgm"), ("parameters", "{}"));
}

public sealed class StopAudioSEffectEntry : PrimitiveEntry
{
    public const string TypeId = "audio.seffect.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("trackId", EntryParameterType.Text), ("instanceId", EntryParameterType.Text));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("trackId", "bgm"));
}

public sealed class PlayVideoEntry : PrimitiveEntry { public const string TypeId = "video.play"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("asset", EntryParameterType.VideoAsset)); }
public sealed class StopVideoEntry : PrimitiveEntry { public const string TypeId = "video.stop"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(); }
