using GalNet.Core.Gallery;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinGalleryModule : PrimitiveModule
{
    public BuiltinGalleryModule(GalleryCatalog? gallery = null) : base(
    [
        BuiltinInstructionFactory.Create<UnlockGalleryEntry>(context => new ImmediatePrimitiveInstance(() =>
            {
                var itemId = Arguments.Int(context, "id");
                if (itemId <= 0) throw new InvalidDataException("gallery.unlock requires a positive integer 'id'.");
                var catalog = gallery ?? throw new InvalidOperationException(
                    "The gallery module requires a GalleryCatalog.");
                if (!catalog.TryGetItem(itemId, out var item))
                    throw new InvalidDataException($"Gallery item '{itemId}' is not declared in the generated Gallery catalog.");
                context.Runtime.SetVariable(GalleryUnlockVariable.GetRuntimeName(item.Id), true);
            }, batchId: context.BatchId))
    ])
    { }
}
