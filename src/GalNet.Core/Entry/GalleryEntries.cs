namespace GalNet.Core.Entry;

public sealed class UnlockGalleryEntry : PrimitiveEntry
{
    public const string TypeId = "unlock_gallery";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("category", EntryParameterType.Select), ("id", EntryParameterType.Integer));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("category", ["Portrait", "Cg", "Scene"]));
}
