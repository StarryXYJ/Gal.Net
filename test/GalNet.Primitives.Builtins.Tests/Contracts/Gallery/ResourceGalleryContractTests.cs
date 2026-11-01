using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;

namespace GeneralTest.Gallery;

public sealed class ResourceGalleryContractTests
{
    [TestCase("cg", "sprite")]
    [TestCase("video", "video")]
    [TestCase("audio", "audio")]
    [TestCase("scene", "group")]
    public void TwoNamedResourcesCompileInUnlockIdOrderAndUnnamedMembershipIsIndependent(string galleryType, string resourceType)
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        AssetMeta Meta(string id, int? unlock, string? key)
        {
            var meta = types.Get(resourceType).Create!(); meta.Id = id; meta.Path = id; meta.TypeId = resourceType;
            meta.GalleryUnlockId = unlock; meta.DisplayNameKey = key; return meta;
        }
        var catalog = new GalleryCatalogCompiler(types, BuiltinGalleryTypes.CreateCatalog(types))
            .Compile([Meta("second", 20, "asset.second.name"), Meta("named-only", null, "asset.hidden.name"), Meta("first", 10, null)]);
        Assert.That(catalog.GetItems(galleryType).Select(item => item.ResourceId), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(catalog.GetItems(galleryType)[0].DisplayNameKey, Is.Null);
        Assert.That(catalog.GetItems(galleryType)[1].DisplayNameKey, Is.EqualTo("asset.second.name"));
    }
    [TestCase("cg", "sprite")]
    [TestCase("video", "video")]
    [TestCase("audio", "audio")]
    [TestCase("scene", "group")]
    public void UnsupportedAppTypesKeepCommonMetadataWithoutEnteringGallery(string galleryType, string resourceType)
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        var meta = types.Get(resourceType).Create!(); meta.Id = "resource"; meta.Path = "resource"; meta.TypeId = resourceType;
        meta.DisplayNameKey = "asset.resource.name"; meta.GalleryUnlockId = 17;
        var codec = new AssetMetaCodec(types); var copy = codec.Deserialize(codec.Serialize(meta));
        var catalog = new GalleryCatalogCompiler(types, new GalleryTypeCatalogBuilder().Build(types)).Compile([copy]);
        Assert.That(catalog.Items, Is.Empty);
        Assert.That(copy.DisplayNameKey, Is.EqualTo(meta.DisplayNameKey));
        Assert.That(copy.GalleryUnlockId, Is.EqualTo(17));
    }
    [Test]
    public void UnlockIdsAreUniqueAcrossUnsupportedAndSupportedResources()
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        var compiler = new GalleryCatalogCompiler(types, BuiltinGalleryTypes.CreateCatalog(types));
        Assert.Throws<InvalidDataException>(() => compiler.Compile([
            new AssetMeta { Id = "font", Path = "font.ttf", TypeId = "font", GalleryUnlockId = 1 },
            new AssetMeta { Id = "music", Path = "music.mp3", TypeId = "audio", GalleryUnlockId = 1 }]));
    }
}
