using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Serialization;
using GalNet.Editor.Shared.Services;
using GalNet.Storage.FileSystem;
using AssetCompressionMode = GalNet.Core.Assets.CompressionMode;

namespace GalNet.Assets.Tests;

public sealed class GameInstallationTests
{
    [Test]
    public async Task Exporter_writes_direct_content_and_installed_resources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-export-{Guid.NewGuid():N}");
        var project = Path.Combine(root, "project");
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        Directory.CreateDirectory(Path.Combine(project, "Graph"));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(project, "Assets", "hero.png"), "hero"u8.ToArray());
            await File.WriteAllTextAsync(Path.Combine(project, "Assets", "hero.png.meta"), """{"id":"hero","type":"sprite","path":"hero.png","filter":"bilinear","compress":"none"}""");
            await File.WriteAllTextAsync(Path.Combine(project, "Graph", "graph.json"), """{"name":"export-test","rootNodeId":"","nodes":[],"edges":[]}""");
            await File.WriteAllTextAsync(Path.Combine(project, "settings.json"), "{}");

            var development = await GameInstallation.OpenAsync(project);
            Assert.That(development.IsPackaged, Is.False);
            Assert.That((await development.CreateContentProvider().LoadAsync()).Graph.Name, Is.EqualTo("export-test"));
            using (var sourceAssets = new AssetManager(development.CreateAssetProviders()))
                Assert.That((await sourceAssets.GetFileAsync("hero"))?.Path, Is.EqualTo("hero.png"));

            var export = await GamePackageExporter.ExportAsync("project", "Sample", project, output);
            Assert.That(export.Success, Is.True, export.Error);

            var installation = await GameInstallation.OpenAsync(export.PackagePath!);
            Assert.That(File.Exists(Path.Combine(installation.RootDirectory, "gallery.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(installation.RootDirectory, "Graph", "graph.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(installation.RootDirectory, "Assets", "Paks", "000-base.pak")), Is.True);
            Assert.That((await installation.CreateContentProvider().LoadAsync()).Graph.Name, Is.EqualTo("export-test"));

            using var assets = new AssetManager(installation.CreateAssetProviders());
            Assert.That((await assets.GetFileAsync("hero"))?.Path, Is.EqualTo("hero.png"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Galpak_installs_loads_content_and_gives_patch_pak_priority()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var types = BuiltinResourceTypes.CreateCatalog();
            var baseAssets = PakBuilder.Build("assets", [Asset("hero", "base")], AssetCompressionMode.None, types);
            var contentFiles = BuildContentFiles(types);
            var packagePath = Path.Combine(root, "sample.galpak");
            await CreatePackageAsync(packagePath, new (string Path, byte[] Data)[]
            {
                ("Assets/Paks/000-base.pak", baseAssets)
            }
                .Concat(contentFiles).ToArray());

            var installation = await GameInstallation.OpenAsync(packagePath);
            Assert.That(installation.IsPackaged, Is.True);
            Assert.That(File.Exists(Path.Combine(installation.RootDirectory, "Assets", "Paks", "000-base.pak")), Is.True);
            Assert.That((await installation.CreateContentProvider().LoadAsync()).Graph.Name, Is.EqualTo("package-test"));

            var resourcePaks = Path.Combine(installation.RootDirectory, "Assets", "Paks");
            await File.WriteAllBytesAsync(Path.Combine(resourcePaks, "100-extra.pak"), PakBuilder.Build("assets", [Asset("hero", "patch")], AssetCompressionMode.None, types));

            using var manager = new AssetManager(installation.CreateAssetProviders(types));
            using var handle = await manager.AcquireAsync<string>("hero");
            Assert.That(handle?.Value, Is.EqualTo("patch"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Galpak_rejects_payloads_whose_manifest_hash_does_not_match()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var packagePath = Path.Combine(root, "broken.galpak");
            var bytes = "not a pak"u8.ToArray();
            var manifest = new GalpakManifest(1, "test", "broken", DateTimeOffset.UtcNow,
                [new GalpakFileEntry("Assets/Paks/000-base.pak", new string('0', 64), bytes.Length)]);
            await using (var file = File.Create(packagePath))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(zip, "Assets/Paks/000-base.pak", bytes);
                await WriteEntryAsync(zip, "broken.galnet", JsonSerializer.SerializeToUtf8Bytes(manifest));
            }

            Assert.ThrowsAsync<InvalidDataException>(() => GalpakInstaller.InstallAsync(packagePath));
            Assert.That(Directory.Exists(Path.Combine(root, "broken")), Is.False);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static GameFile Asset(string id, string value) => new(id, "hero.png", "sprite",
        new SpriteAssetMeta { Id = id, TypeId = "sprite", Path = "hero.png" }, System.Text.Encoding.UTF8.GetBytes(value));

    private static IReadOnlyList<(string Path, byte[] Data)> BuildContentFiles(IResourceTypeCatalog types)
    {
        var galleryTypes = BuiltinGalleryTypes.CreateCatalog(types);
        var gallery = new GalleryConfiguration { Types = galleryTypes.Types.ToList(), Items = [] };
        return [
            ("Graph/graph.json", System.Text.Encoding.UTF8.GetBytes("{\"name\":\"package-test\",\"rootNodeId\":\"\",\"nodes\":[],\"edges\":[]}")),
            ("gallery.json", JsonSerializer.SerializeToUtf8Bytes(gallery))
        ];
    }

    private static async Task CreatePackageAsync(string path, IReadOnlyList<(string Path, byte[] Data)> files)
    {
        var entries = files.Select(file => new GalpakFileEntry(file.Path, Hash(file.Data), file.Data.Length)).ToArray();
        var manifest = new GalpakManifest(1, "test", "sample", DateTimeOffset.UtcNow, entries);
        await using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var entry in files) await WriteEntryAsync(zip, entry.Path, entry.Data);
        await WriteEntryAsync(zip, "sample.galnet", JsonSerializer.SerializeToUtf8Bytes(manifest));
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string path, byte[] data)
    {
        var entry = zip.CreateEntry(path);
        await using var stream = entry.Open();
        await stream.WriteAsync(data);
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
