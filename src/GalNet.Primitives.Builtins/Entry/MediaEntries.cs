namespace GalNet.Core.Entry;

public sealed class PlayAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.play";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("channel", EntryParameterType.Select), ("asset", EntryParameterType.AudioAsset), ("volume", EntryParameterType.Float), ("mode", EntryParameterType.Select), ("times", EntryParameterType.Integer));
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

public sealed class PauseAudioEntry : PrimitiveEntry { public const string TypeId = "audio.pause"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => StopAudioEntry.ParameterTypes; public static IReadOnlyDictionary<string, string> DefaultValues => StopAudioEntry.DefaultValues; public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => StopAudioEntry.ParameterOptions; }
public sealed class ResumeAudioEntry : PrimitiveEntry { public const string TypeId = "audio.resume"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => StopAudioEntry.ParameterTypes; public static IReadOnlyDictionary<string, string> DefaultValues => StopAudioEntry.DefaultValues; public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => StopAudioEntry.ParameterOptions; }

public sealed class EnqueueAudioEntry : PrimitiveEntry
{
    public const string TypeId = "audio.enqueue";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("channel", EntryParameterType.Select), ("asset", EntryParameterType.AudioAsset), ("times", EntryParameterType.Integer));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("channel", "bgm"), ("times", "1"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => StopAudioEntry.ParameterOptions;
}

public sealed class PlayVideoEntry : PrimitiveEntry { public const string TypeId = "video.play"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("asset", EntryParameterType.VideoAsset)); }
public sealed class StopVideoEntry : PrimitiveEntry { public const string TypeId = "video.stop"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(); }
