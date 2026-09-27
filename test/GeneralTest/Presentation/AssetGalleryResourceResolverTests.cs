using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;

namespace GeneralTest.Presentation;

public sealed class AssetGalleryResourceResolverTests
{
    [Test]
    public async Task Resolver_materializes_media_acquired_from_the_asset_manager()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-gallery-resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "opening.png"), [1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(root, "opening.png.meta"), """{"id":"asset-opening","type":"sprite","path":"opening.png","filter":"bilinear","compress":"none"}""");
            var gallery = GalleryCatalog.Create(new GalleryConfiguration
            {
                Types = [new GalleryTypeRegistration { TypeId = "cg", ResourceTypeId = "sprite" }],
                Items = [new GalleryItem { Id = 1, TypeId = "cg", ResourceId = "asset-opening" }]
            });

            using var assets = new AssetManager([new LocalFileProvider(root)]);
            using var resolver = await AssetGalleryResourceResolver.CreateAsync(assets, gallery);
            var path = resolver.ResolvePath("asset-opening");

            Assert.That(path, Is.Not.Null);
            Assert.That(await File.ReadAllBytesAsync(path!), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
