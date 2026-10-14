using GalNet.Core.Entry;

namespace GalNet.Contracts.Builtins;

public sealed class UnlockGalleryEntry : PrimitiveEntry
{
    public const string TypeId = "gallery.unlock";
    public override string Type => TypeId;
}
