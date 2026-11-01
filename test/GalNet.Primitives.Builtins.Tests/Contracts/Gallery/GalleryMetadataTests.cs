using GalNet.Core.Assets;
namespace GeneralTest.Gallery;
public sealed class GalleryMetadataTests
{
    [Test]
    public void CommonMetadataRetainsNamesUnlockIdentityAndUnknownMembers()
    {
        var codec = new AssetMetaCodec(new ResourceTypeCatalogBuilder().Add<AssetMeta>("project").Build());
        var metadata = codec.Deserialize("""{"id":"item","type":"project","path":"file","future":true,"galleryUnlockId":3,"displayNameKey":"asset.item.name"}""");
        metadata.GalleryUnlockId = 4;
        var reloaded = codec.Deserialize(codec.Serialize(metadata));
        Assert.That(reloaded.GalleryUnlockId, Is.EqualTo(4));
        Assert.That(reloaded.DisplayNameKey, Is.EqualTo("asset.item.name"));
        Assert.That(reloaded.ExtensionData!["future"].GetBoolean(), Is.True);
    }
    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("\"invalid\"")]
    public void CommonUnlockIdentityCannotBeSilentlyDiscarded(string value)
    {
        var codec = new AssetMetaCodec(new ResourceTypeCatalogBuilder().Add<AssetMeta>("project").Build());
        Assert.Throws<InvalidDataException>(() => codec.Deserialize("{\"id\":\"item\",\"type\":\"project\",\"path\":\"file\",\"galleryUnlockId\":" + value + "}"));
    }
}
