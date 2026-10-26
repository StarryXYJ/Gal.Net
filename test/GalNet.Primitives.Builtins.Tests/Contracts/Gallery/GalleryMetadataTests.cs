using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Gallery;

namespace GeneralTest.Gallery;

public sealed class GalleryMetadataTests
{
    [Test]
    public void OptionalGalleryExtensionRetainsUnknownMetadataAndAnnotationMembers()
    {
        var codec = new AssetMetaCodec(new ResourceTypeCatalogBuilder().Add<AssetMeta>("project").Build());
        var metadata = codec.Deserialize("""{"id":"item","type":"project","path":"file","future":true,"gallery":[{"id":3,"typeId":"images","custom":{"kept":true}}]}""");
        metadata.SetGallery(metadata.ReadGallery().Select(item => item with { Id = 4 }));
        var reloaded = codec.Deserialize(codec.Serialize(metadata));
        Assert.That(reloaded.ReadGallery().Single().Id, Is.EqualTo(4));
        Assert.That(reloaded.ReadGallery().Single().ExtensionData!["custom"].GetProperty("kept").GetBoolean(), Is.True);
        Assert.That(reloaded.ExtensionData!["future"].GetBoolean(), Is.True);
    }
}
