using System.Text.Json;
using GalNet.Core.Entry;

namespace GalNet.Primitives.Builtins;

/// <summary>Authoring-only dialogue entry that expands to audio followed by a normal blocking text primitive.</summary>
public sealed class TextVoiceEntry : CompositeEntry
{
    public const string TypeId = "dialogue.textVoice";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("speaker", EntryParameterType.Autocomplete), ("content", EntryParameterType.MultilineText), ("asset", EntryParameterType.AudioAsset), ("gain", EntryParameterType.Float), ("trackId", EntryParameterType.Text));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("gain", "1"), ("trackId", "vocal"));

    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context) =>
    [
        VoiceTextEntrySupport.Audio(context, this),
        VoiceTextEntrySupport.Text(context, this)
    ];
}

/// <summary>Authoring-only NVL entry that expands to audio followed by a normal blocking append primitive.</summary>
public sealed class AppendVoiceNvlEntry : CompositeEntry
{
    public const string TypeId = "nvl.appendVoice";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("content", EntryParameterType.MultilineText), ("asset", EntryParameterType.AudioAsset), ("gain", EntryParameterType.Float), ("trackId", EntryParameterType.Text));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("gain", "1"), ("trackId", "vocal"));

    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context) =>
    [
        VoiceTextEntrySupport.Audio(context, this),
        VoiceTextEntrySupport.Nvl(context, this)
    ];
}

internal static class VoiceTextEntrySupport
{
    public static PrimitiveEntry Audio(EntryCompileContext context, Entry source) => new(
        PlayAudioEntry.TypeId,
        JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["trackId"] = Get(source, "trackId", "vocal"),
            ["asset"] = Required(source, "asset"),
            ["gain"] = Float(source, "gain", 1),
            ["placement"] = "replaceIfDifferent",
            ["autoBlocking"] = "true",
            ["fadeIn"] = 0
        }))
    { Condition = context.Condition };

    public static PrimitiveEntry Text(EntryCompileContext context, Entry source) => new(
        TextEntry.TypeId,
        JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["speaker"] = Get(source, "speaker", ""),
            ["content"] = Get(source, "content", "")
        }))
    { Condition = context.Condition };

    public static PrimitiveEntry Nvl(EntryCompileContext context, Entry source) => new(
        AppendNvlEntry.TypeId,
        JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["content"] = Get(source, "content", "") }))
    { Condition = context.Condition };

    private static string Get(Entry source, string key, string fallback) => source.Values.GetValueOrDefault(key, fallback);
    private static string Required(Entry source, string key) => !string.IsNullOrWhiteSpace(Get(source, key, "")) ? Get(source, key, "") : throw new InvalidDataException($"'{source.Type}' requires '{key}'.");
    private static float Float(Entry source, string key, float fallback) => float.TryParse(Get(source, key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture, out var value) && float.IsFinite(value) ? value : throw new InvalidDataException($"'{source.Type}.{key}' must be numeric.");
}
