using System.IO.Compression;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Entry;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Tests;

public sealed class ExportPreparationTests
{
    private string _root = null!;
    private string Project => Path.Combine(_root, "project");
    private readonly TargetProfileEntryCatalog _entries = new([]);

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "galnet-export-preparation-" + Guid.NewGuid().ToString("N"));
        Put("Graph/graph.json", """{"version":2,"name":"Game","rootNodeId":"","nodes":[],"edges":[],"playerVariables":[],"saveVariables":[]}""");
        Put("Assets/pose.png", "sprite bytes");
        Put("Assets/pose.png.meta", """{"id":"pose","type":"sprite"}""");
        Put("Assets/character.variants", """{"version":1,"folder":".","default":"pose"}""");
        Put("Assets/character.variants.meta", """{"id":"character","type":"image-variants"}""");
    }

    [TearDown] public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public async Task DirectoryAndArchiveExportsContainIdenticalCompiledPayloads()
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        var gallery = BuiltinGalleryTypes.CreateCatalog(types);
        var directory = await GamePackageExporter.ExportDirectoryAsync("id", "Game", Project, Path.Combine(_root, "directory"), types, gallery, _entries);
        var package = await GamePackageExporter.ExportAsync("id", "Game", Project, Path.Combine(_root, "packages"), types, gallery, _entries);
        Assert.That(directory.Success, Is.True, directory.Error);
        Assert.That(package.Success, Is.True, package.Error);
        using var zip = ZipFile.OpenRead(package.PackagePath!);
        foreach (var item in zip.Entries.Where(item => !item.FullName.EndsWith(".galnet", StringComparison.Ordinal)))
        {
            using var input = item.Open();
            using var bytes = new MemoryStream();
            await input.CopyToAsync(bytes);
            Assert.That(File.ReadAllBytes(Path.Combine(directory.ContentDirectory!, item.FullName)), Is.EqualTo(bytes.ToArray()), item.FullName);
        }
        using var archive = Archive.Deserialize("assets", File.ReadAllBytes(Path.Combine(directory.ContentDirectory!, "Assets/Paks/000-base.pak")), types);
        var variants = ImageVariantsCodec.Decode(archive.GetAsset("character")!.ReadAllBytes());
        Assert.That(variants.Default.AssetId, Is.EqualTo("pose"));
    }

    [Test]
    public async Task InvalidStagedAssetsPreserveExistingBuildAndPackageAndDoNotPublishDirectory()
    {
        Put("Assets/character.variants", """{"version":1,"folder":"../escape","default":"pose"}""");
        var types = BuiltinResourceTypes.CreateCatalog();
        var gallery = BuiltinGalleryTypes.CreateCatalog(types);
        var output = Path.Combine(_root, "build");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "sentinel"), "keep");
        Assert.ThrowsAsync<InvalidDataException>(() => new ProjectContentBuilder(_entries, types).BuildAsync(Project, output));
        var packages = Path.Combine(_root, "packages");
        Directory.CreateDirectory(packages);
        File.WriteAllText(Path.Combine(packages, "Game.galpak"), "previous");
        var package = await GamePackageExporter.ExportAsync("id", "Game", Project, packages, types, gallery, _entries);
        var directory = await GamePackageExporter.ExportDirectoryAsync("id", "Game", Project, Path.Combine(_root, "directory"), types, gallery, _entries);
        Assert.That(package.Success, Is.False);
        Assert.That(directory.Success, Is.False);
        Assert.That(File.ReadAllText(Path.Combine(output, "sentinel")), Is.EqualTo("keep"));
        Assert.That(File.ReadAllText(Path.Combine(packages, "Game.galpak")), Is.EqualTo("previous"));
        Assert.That(Directory.Exists(Path.Combine(_root, "directory")), Is.False);
        Assert.That(Directory.GetDirectories(_root, ".*.build-*"), Is.Empty);
    }

    private void Put(string relative, string text)
    {
        var path = Path.Combine(Project, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
