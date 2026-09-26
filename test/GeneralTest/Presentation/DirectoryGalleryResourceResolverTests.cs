using System.Text.Json;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Assets;

namespace GeneralTest.Presentation;

public sealed class DirectoryGalleryResourceResolverTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"galnet-gallery-resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_directory, "nested"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Test]
    public void ResolverFindsAssetBesideMetadataAnywhereUnderTheProvidedRoot()
    {
        var assetPath = Path.Combine(_directory, "nested", "opening.png");
        File.WriteAllBytes(assetPath, [1, 2, 3]);
        File.WriteAllText(assetPath + ".meta", JsonSerializer.Serialize(new SpriteAssetMeta
        {
            Id = "asset-opening",
            TypeId = "sprite"
        }));

        var resolver = new DirectoryGalleryResourceResolver(_directory);

        Assert.That(resolver.ResolvePath("asset-opening"), Is.EqualTo(assetPath));
    }
}
