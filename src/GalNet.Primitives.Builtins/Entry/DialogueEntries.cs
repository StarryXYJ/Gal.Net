namespace GalNet.Core.Entry;

public sealed class TextEntry : PrimitiveEntry
{
    public const string TypeId = "text";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("speaker", EntryParameterType.Autocomplete), ("content", EntryParameterType.MultilineText), ("voice", EntryParameterType.AudioAsset));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults();
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
