namespace GalNet.Core.Entry;

public sealed class UnlockGalleryEntry : PrimitiveEntry
{
    public const string TypeId = "gallery.unlock";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } =
        EntrySchema.Parameters(("id", EntryParameterType.Integer));
}
