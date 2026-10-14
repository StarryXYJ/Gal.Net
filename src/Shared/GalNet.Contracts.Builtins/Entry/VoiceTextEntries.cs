using GalNet.Core.Audio;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

internal static class VoiceTextEntrySupport
{
    public static IReadOnlyList<PrimitiveEntry> Text(EntryCompileContext context, TextVoiceOptions options) =>
    [
        Audio(context, options.Asset, options.TrackId, options.Gain, BuiltinEntryIds.TextVoice),
        new PrimitiveEntry(BuiltinEntryIds.Text, ValueCodec.SerializeFields(new DialogueTextOptions
        { Speaker = options.Speaker ?? "", Content = options.Content ?? "" })) { Condition = context.Condition }
    ];
    public static IReadOnlyList<PrimitiveEntry> Nvl(EntryCompileContext context, NvlVoiceOptions options) =>
    [
        Audio(context, options.Asset, options.TrackId, options.Gain, BuiltinEntryIds.AppendVoiceNvl),
        new PrimitiveEntry(BuiltinEntryIds.AppendNvl, ValueCodec.SerializeFields(new NvlAppendOptions
        { Content = options.Content ?? "" })) { Condition = context.Condition }
    ];
    private static PrimitiveEntry Audio(EntryCompileContext context, string? asset, string? track, float gain, string type)
    {
        if (string.IsNullOrWhiteSpace(asset)) throw new InvalidDataException($"'{type}' requires 'asset'.");
        return new(BuiltinEntryIds.PlayAudio, ValueCodec.SerializeFields(new AudioPlayOptions
        { TrackId = track ?? "", Asset = asset, Gain = gain, Placement = AudioPlacement.ReplaceIfDifferent, AutoBlocking = true }))
        { Condition = context.Condition };
    }
}
