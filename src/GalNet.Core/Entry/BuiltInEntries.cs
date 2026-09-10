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

public sealed class TextEntry : Entry
{
    public const string TypeId = "text";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("speaker", EntryParameterType.Autocomplete), ("content", EntryParameterType.MultilineText),
        ("voice", EntryParameterType.AudioAsset));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults();
}

public sealed class ShowLayerEntry : Entry
{
    public const string TypeId = "layer.show";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("handleId", EntryParameterType.Text), ("assetId", EntryParameterType.ImageAsset), ("transform", EntryParameterType.Json),
        ("z", EntryParameterType.Float), ("displayMode", EntryParameterType.Select), ("transitionId", EntryParameterType.Text),
        ("transitionDuration", EntryParameterType.Float), ("transitionBlocking", EntryParameterType.Select), ("transitionParameters", EntryParameterType.MultilineText));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transform", "{}"), ("z", "0"), ("displayMode", "Native"), ("transitionDuration", "0.5"), ("transitionBlocking", "false"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("transitionBlocking", ["false", "true"]), ("displayMode", ["Native", "Tile", "Fill", "Uniform", "UniformToFill"]));
}

public sealed class HideLayerEntry : Entry
{
    public const string TypeId = "layer.hide";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("handleId", EntryParameterType.Text), ("transitionId", EntryParameterType.Text), ("transitionDuration", EntryParameterType.Float),
        ("transitionBlocking", EntryParameterType.Select), ("transitionParameters", EntryParameterType.MultilineText));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transitionDuration", "0.5"), ("transitionBlocking", "false"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = ShowLayerEntry.ParameterOptions;
}

public sealed class MoveLayerEntry : Entry
{
    public const string TypeId = "layer.move";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("handleId", EntryParameterType.Text), ("transform", EntryParameterType.Json),
        ("z", EntryParameterType.Float), ("duration", EntryParameterType.Float));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transform", "{}"), ("z", "0"), ("duration", "0.5"));
}

public sealed class ReplaceLayerEntry : Entry
{
    public const string TypeId = "layer.replace";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("handleId", EntryParameterType.Text), ("assetId", EntryParameterType.ImageAsset));
}

public sealed class PlayAudioEntry : Entry
{
    public const string TypeId = "audio.play";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("channel", EntryParameterType.Select), ("asset", EntryParameterType.AudioAsset), ("volume", EntryParameterType.Float),
        ("mode", EntryParameterType.Select), ("times", EntryParameterType.Integer));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("channel", "bgm"), ("volume", "0.8"), ("mode", "once"), ("times", "1"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("channel", ["bgm", "sfx", "voice"]), ("mode", ["once", "loop"]));
}

public sealed class StopAudioEntry : Entry
{
    public const string TypeId = "audio.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("channel", EntryParameterType.Select));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("channel", "bgm"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("channel", ["bgm", "sfx", "voice"]));
}

public sealed class PauseAudioEntry : Entry
{
    public const string TypeId = "audio.pause";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => StopAudioEntry.ParameterTypes;
    public static IReadOnlyDictionary<string, string> DefaultValues => StopAudioEntry.DefaultValues;
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => StopAudioEntry.ParameterOptions;
}

public sealed class ResumeAudioEntry : Entry
{
    public const string TypeId = "audio.resume";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => StopAudioEntry.ParameterTypes;
    public static IReadOnlyDictionary<string, string> DefaultValues => StopAudioEntry.DefaultValues;
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => StopAudioEntry.ParameterOptions;
}

public sealed class EnqueueAudioEntry : Entry
{
    public const string TypeId = "audio.enqueue";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("channel", EntryParameterType.Select), ("asset", EntryParameterType.AudioAsset), ("times", EntryParameterType.Integer));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("channel", "bgm"), ("times", "1"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions => StopAudioEntry.ParameterOptions;
}

public sealed class PlayVideoEntry : Entry
{
    public const string TypeId = "video.play";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("asset", EntryParameterType.VideoAsset));
}

public sealed class StopVideoEntry : Entry
{
    public const string TypeId = "video.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters();
}

public sealed class ShowDialogueEntry : Entry
{
    public const string TypeId = "dialogue.show";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters();
}

public sealed class HideDialogueEntry : Entry
{
    public const string TypeId = "dialogue.hide";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes => ShowDialogueEntry.ParameterTypes;
}

public sealed class ApplyEffectEntry : Entry
{
    public const string TypeId = "effect.apply";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("id", EntryParameterType.Text), ("instanceId", EntryParameterType.Text), ("duration", EntryParameterType.Float),
        ("blocking", EntryParameterType.Select), ("parameters", EntryParameterType.MultilineText));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("duration", "-1"), ("blocking", "false"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("blocking", ["false", "true"]));
}

public sealed class StopEffectEntry : Entry
{
    public const string TypeId = "effect.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("instanceId", EntryParameterType.Text));
}

public sealed class WaitEntry : Entry
{
    public const string TypeId = "wait";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("duration", EntryParameterType.Float));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("duration", "1"));
}

public sealed class SetVariableEntry : Entry
{
    public const string TypeId = "variable.set";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("target", EntryParameterType.VariableName), ("expression", EntryParameterType.Expression));
}

public sealed class UnlockGalleryEntry : Entry
{
    public const string TypeId = "unlock_gallery";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("category", EntryParameterType.Select), ("id", EntryParameterType.Integer));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("category", ["Portrait", "Cg", "Scene"]));
}
