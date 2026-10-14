using GalNet.Core.Entry;

namespace GalNet.Contracts.Builtins;

public sealed class TextEntry : PrimitiveEntry
{
    public const string TypeId = "dialogue.text";
    public override string Type => TypeId;
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
