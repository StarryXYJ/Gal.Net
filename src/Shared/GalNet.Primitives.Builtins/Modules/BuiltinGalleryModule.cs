using GalNet.Contracts.Builtins;
using GalNet.Core.Gallery;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinGalleryModule : PrimitiveModule
{
    public BuiltinGalleryModule(GalleryCatalog? gallery = null) : base(
    [
        PrimitiveImplementation.Immediate<GalleryUnlockOptions>(BuiltinEntryIds.UnlockGallery, (context, options) =>
            {
                var itemId = options.Id;
                if (itemId <= 0) throw new InvalidDataException("gallery.unlock requires a positive integer 'id'.");
                var catalog = gallery ?? throw new InvalidOperationException(
                    "The gallery module requires a GalleryCatalog.");
                if (!catalog.TryGetItem(itemId, out var item))
                    throw new InvalidDataException($"Gallery item '{itemId}' is not declared in the generated Gallery catalog.");
                context.Runtime.SetVariable(GalleryUnlockVariable.GetRuntimeName(item.Id), true);
            })
    ])
    { }
}
