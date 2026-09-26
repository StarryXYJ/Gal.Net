using System.IO.Compression;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Editor.Shared.Services;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Runtime;

public sealed class GalleryContentLoadingTests
{
    [Test]
    public async Task DirectoryProviderLoadsOptionalGalleryCatalog()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "graph.json"), MinimalGraph);
            await File.WriteAllTextAsync(Path.Combine(directory, "gallery.json"), GalleryJson);

            var content = await new DirectoryGameContentProvider(directory).LoadAsync();

            Assert.Multiple(() =>
            {
                Assert.That(content.Gallery.Types, Has.Count.EqualTo(1));
                Assert.That(content.Gallery.GetItems("cg"), Has.Count.EqualTo(1));
                Assert.That(content.Gallery.GetItems("cg")[0].ResourceId, Is.EqualTo("image-id"));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task DirectoryProviderUsesEmptyCatalogWhenGalleryFileIsAbsent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "graph.json"), MinimalGraph);

            var content = await new DirectoryGameContentProvider(directory).LoadAsync();

            Assert.That(content.Gallery.Items, Is.Empty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task PackageExporterIncludesGalleryJsonInContentPak()
    {
        var directory = CreateTemporaryDirectory();
        var output = Path.Combine(directory, "Output");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "Assets"));
            await WriteAssetAsync(directory, "image-id", "sprite");
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(directory, "gallery.json"), GalleryJson);

            var result = await GamePackageExporter.ExportAsync("test", "test", directory, output);

            Assert.That(result.Success, Is.True, result.Error);
            await using var packageFile = File.OpenRead(result.PackagePath!);
            using var package = new ZipArchive(packageFile, ZipArchiveMode.Read);
            var contentEntry = package.GetEntry("Assets/content.pak");
            Assert.That(contentEntry, Is.Not.Null);
            await using var contentStream = contentEntry!.Open();
            using var contentBytes = new MemoryStream();
            await contentStream.CopyToAsync(contentBytes);
            using var archive = Archive.Deserialize("content", contentBytes.ToArray());

            Assert.That(archive.GetAssetByPath("gallery.json"), Is.Not.Null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task PackageExporterRejectsGalleryItemWhoseAssetWasDeleted()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "Assets"));
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(directory, "gallery.json"), GalleryJson);

            var result = await GamePackageExporter.ExportAsync("test", "test", directory, Path.Combine(directory, "Output"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("opening").And.Contain("image-id"));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WriteAssetAsync(string directory, string id, string type)
    {
        var assetPath = Path.Combine(directory, "Assets", "image.bin");
        await File.WriteAllBytesAsync(assetPath, [1, 2, 3]);
        await File.WriteAllTextAsync(assetPath + ".meta", JsonSerializer.Serialize(new
        {
            Id = id,
            Type = type,
            Path = "image.bin",
            Compress = "none"
        }));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"galnet-gallery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private const string MinimalGraph = """
        {
          "version": 1,
          "name": "Gallery test",
          "rootNodeId": "entry",
          "nodes": [{ "id": "entry", "type": "Entry", "name": "Entry" }],
          "edges": []
        }
        """;

    private const string GalleryJson = """
        {
          "version": 1,
          "types": [{ "typeId": "cg", "resourceType": "sprite" }],
          "items": [{ "id": "opening", "typeId": "cg", "resourceId": "image-id", "title": "Opening" }]
        }
        """;
}
