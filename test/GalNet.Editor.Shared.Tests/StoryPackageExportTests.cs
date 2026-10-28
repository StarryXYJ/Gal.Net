using System.Text.Json;
using GalNet.Assets;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;
using GalNet.Editor.Shared.Services;
using GalNet.GameHost;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Runtime;
using GalNet.Storage.FileSystem;

namespace GalNet.Editor.Shared.Tests;

public sealed class StoryPackageExportTests
{
    private string _root = null!;
    private string Source => Path.Combine(_root, "source");
    private string Installed => Path.Combine(_root, "installed");
    private readonly IResourceTypeCatalog _types = BuiltinResourceTypes.CreateCatalog();
    private readonly IEntryCatalog _catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "galnet-story-packages-" + Guid.NewGuid().ToString("N"));
        Put("Graph/graph.json", """
            {"version":2,"name":"Story","rootNodeId":"base","nodes":[
             {"id":"base","name":"Base","type":"Group","file":"groups/base.rawgalgroup"},
             {"id":"a","name":"A","type":"Group","file":"groups/a.rawgalgroup","storyPackage":"A"},
             {"id":"b","name":"B","type":"Group","file":"groups/b.rawgalgroup","storyPackage":"B"},
             {"id":"end","name":"End","type":"Group","file":"groups/end.rawgalgroup"}],
             "edges":[{"id":"e1","fromNodeId":"base","fromOutlet":0,"toNodeId":"a"},
             {"id":"e2","fromNodeId":"a","fromOutlet":0,"toNodeId":"b"},
             {"id":"e3","fromNodeId":"b","fromOutlet":0,"toNodeId":"end"}],"playerVariables":[],"saveVariables":[]}
            """);
        Group("base", "shared-base");
        Group("a", "shared-base", "shared-extra", "a-only");
        Group("b", "shared-extra");
        Group("end");
        foreach (var id in new[] { "shared-base", "shared-extra", "a-only", "unused" })
        {
            Put("Assets/" + id + ".png", "image-bytes");
            Put("Assets/" + id + ".png.meta", JsonSerializer.Serialize(new { id, type = "sprite" }));
        }
        var configuration = new ProjectConfiguration();
        configuration.Set(ContentPackagingOptions.Section, new() { MaxPakBytes = 1 });
        Put("settings.json", JsonSerializer.Serialize(configuration));
    }
    [TearDown] public void Cleanup() => Directory.Delete(_root, true);

    [Test]
    public async Task EveryInstallationCombinationRunsAndIndependentPackagesCarryTheirOwnSharedExtras()
    {
        var files = await Export();
        var baseline = Directory.GetFiles(Path.Combine(Installed, "Assets", "Paks"), "*.pak").SelectMany(Ids).ToArray();
        Assert.That(baseline, Does.Contain("shared-base").And.Not.Contain("shared-extra").And.Not.Contain("unused"));
        foreach (var package in new[] { "A", "B" })
        {
            var own = files.Where(file => file.Package == package).SelectMany(file => Ids(file.Path)).ToArray();
            Assert.That(own, Does.Contain("shared-extra").And.Not.Contain("shared-base"));
        }
        foreach (var mask in Enumerable.Range(0, 4))
        {
            SetInstalled(files, mask);
            var content = await Load();
            var observed = new List<string>();
            using var registration = new GameRegistration();
            registration.UseCatalog(_catalog);
            registration.RegisterImplementation(new PrimitiveModule(registry => registry.Register("layer.show", context =>
                new ImmediatePrimitiveInstance(() => observed.Add(context.Runtime.CurrentNodeId)))));
            await using var view = registration.BuildView();
            await using var run = new GameRun(content.Graph, new GameRuntime(null, content.Graph.RootNodeId), view);
            await run.PrepareAsync(null);
            await run.StartPreparedAsync();
            var expected = new List<string> { "base" };
            if ((mask & 1) != 0) expected.AddRange(["a", "a", "a"]);
            if ((mask & 2) != 0) expected.Add("b");
            Assert.That(observed, Is.EqualTo(expected), "mask=" + mask);
        }
        using var layout = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Installed, "packaging-layout.json")));
        Assert.That(layout.RootElement.GetProperty("unused").EnumerateArray().Select(item => item.GetProperty("id").GetString()), Does.Contain("unused"));
    }

    [Test]
    public async Task ResourcePatchExportsBeforeBaseAndSessionsFreezeTheSelectedOverrideSet()
    {
        Put("Assets/coat.png", "coat-B");
        Put("Assets/coat.png.meta", """{"id":"coat","type":"sprite"}""");
        var definition = new ResourceOverrideDefinition { Id = "clothes", Mappings = new() { ["shared-base"] = "coat", ["a-only"] = "coat" } };
        var patchPath = Path.Combine(_root, "independent.pak");
        var sourceMeta = File.ReadAllBytes(Path.Combine(Source, "Assets/coat.png.meta"));
        await ResourceOverrideExporter.ExportAsync("story-project", Source, definition, patchPath, _types);
        var firstBytes = File.ReadAllBytes(patchPath);
        await ResourceOverrideExporter.ExportAsync("story-project", Source, definition, patchPath, _types);
        Assert.That(File.ReadAllBytes(patchPath), Is.EqualTo(firstBytes));
        Assert.That(File.ReadAllBytes(Path.Combine(Source, "Assets/coat.png.meta")), Is.EqualTo(sourceMeta));
        using (var patch = Archive.Deserialize("assets", firstBytes, _types))
        {
            Assert.That(patch.Descriptor.Kind, Is.EqualTo("override"));
            Assert.That(patch.AssetIds, Is.EquivalentTo(new[] { "coat" }));
        }
        var stories = await Export(); SetInstalled(stories, 0);
        var patchDirectory = Path.Combine(Installed, "ResourcePatches"); Directory.CreateDirectory(patchDirectory);
        File.Copy(patchPath, Path.Combine(patchDirectory, "renamed.pak"));
        File.Copy(patchPath, Path.Combine(patchDirectory, "duplicate.pak"));
        var installation = await GameInstallation.OpenAsync(Installed);
        Assert.That((await installation.CreateContentProvider(_types, BuiltinGalleryTypes.CreateCatalog(_types)).LoadAsync()).Graph.InstalledStoryPackages, Is.Empty);
        using var assets = new AssetManager(installation.CreateAssetProviders(_types));
        using var oldHandle = await assets.AcquireAsync<string>("shared-base");
        Assert.That(oldHandle!.Value, Is.EqualTo("coat-B"));
        Assert.That(await assets.GetFileAsync("a-only"), Is.Null, "An override cannot install an absent story's original resource.");
        Put("Assets/coat-c.png", "coat-C"); Put("Assets/coat-c.png.meta", """{"id":"coat-c","type":"sprite"}""");
        var high = new ResourceOverrideDefinition { Id = "higher", Priority = 1, Mappings = new() { ["shared-base"] = "coat-c" } };
        await ResourceOverrideExporter.ExportAsync("story-project", Source, high, Path.Combine(patchDirectory, "higher.pak"), _types);
        using var frozen = new AssetManager(installation.CreateAssetProviders(_types));
        using var frozenHandle = await frozen.AcquireAsync<string>("shared-base");
        Assert.That(frozenHandle!.Value, Is.EqualTo("coat-B"));
        var reopened = await GameInstallation.OpenAsync(Installed);
        using var updated = new AssetManager(reopened.CreateAssetProviders(_types));
        using var updatedHandle = await updated.AcquireAsync<string>("shared-base");
        Assert.That(updatedHandle!.Value, Is.EqualTo("coat-C"));
        Assert.That(oldHandle.Value, Is.EqualTo("coat-B"));
        var invalid = File.ReadAllBytes(Path.Combine(patchDirectory, "higher.pak")); invalid[^1] ^= 1;
        File.WriteAllBytes(Path.Combine(patchDirectory, "higher.pak"), invalid);
        var corrupted = await GameInstallation.OpenAsync(Installed);
        Assert.Throws<InvalidDataException>(() => corrupted.CreateAssetProviders(_types));
    }

    [Test]
    public async Task GalpakPublishesExternalStoriesAndRepeatExportKeepsContentHashes()
    {
        var output = Path.Combine(_root, "release");
        async Task<GamePackageExportResult> ExportZip() => await GamePackageExporter.ExportAsync("story-project", "Story", Source, output,
            _types, BuiltinGalleryTypes.CreateCatalog(_types), _catalog);
        var first = await ExportZip();
        Assert.That(first.Success, Is.True, first.Error);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(first.PackagePath!))
            Assert.That(zip.Entries.Any(entry => entry.FullName.StartsWith("StoryPatches/", StringComparison.Ordinal)), Is.False);
        var parts = Directory.GetFiles(Path.Combine(output, "StoryPatches"), "*.pak", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => CryptoHelper.HashSHA256(File.ReadAllBytes(path)));
        var second = await ExportZip();
        Assert.That(second.Success, Is.True, second.Error);
        foreach (var part in parts) Assert.That(CryptoHelper.HashSHA256(File.ReadAllBytes(part.Key)), Is.EqualTo(part.Value));
        var installation = await GameInstallation.OpenAsync(second.PackagePath!);
        var content = await installation.CreateContentProvider(_types, BuiltinGalleryTypes.CreateCatalog(_types)).LoadAsync();
        Assert.That(content.Graph.InstalledStoryPackages, Is.EquivalentTo(new[] { "A", "B" }));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var before = File.ReadAllBytes(second.PackagePath!);
        var canceledResult = await GamePackageExporter.ExportAsync("story-project", "Story", Source, output,
            _types, BuiltinGalleryTypes.CreateCatalog(_types), _catalog, cancellationToken: canceled.Token);
        Assert.That(canceledResult.Success, Is.False);
        Assert.That(File.ReadAllBytes(second.PackagePath!), Is.EqualTo(before));
    }

    [Test]
    public async Task PartialCorruptAndMissingSavePackagesFailButRenamingStoryPartsDoesNot()
    {
        var files = await Export();
        var a = files.Where(file => file.Package == "A").ToArray();
        Assert.That(a.Length, Is.GreaterThan(1));
        File.Delete(a[0].Path);
        Assert.ThrowsAsync<InvalidDataException>(() => Load());
        SetInstalled(files, 3);
        var corrupt = a[0].Bytes.ToArray(); corrupt[^1] ^= 1;
        File.WriteAllBytes(a[0].Path, corrupt);
        Assert.ThrowsAsync<InvalidDataException>(() => Load());
        SetInstalled(files, 3);
        File.Move(a[0].Path, Path.Combine(Path.GetDirectoryName(a[0].Path)!, "renamed.pak"));
        var content = await Load();
        using var registration = new GameRegistration();
        registration.UseCatalog(_catalog);
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Register("layer.show", _ => new ImmediatePrimitiveInstance(() => { }))));
        await using var view = registration.BuildView();
        await using var run = new GameRun(content.Graph, new GameRuntime(null, "base"), view);
        await run.PrepareAsync(null);
        var save = run.CreateSaveData();
        Assert.That(save.StoryPackages, Is.EquivalentTo(new[] { "A", "B" }));
        File.Delete(Path.Combine(Path.GetDirectoryName(a[0].Path)!, "renamed.pak"));
        SetInstalled(files, 2);
        var missing = await Load();
        await using var replacement = new GameRun(missing.Graph, new GameRuntime(null, "base"), view);
        Assert.ThrowsAsync<InvalidDataException>(() => replacement.ValidateSnapshotAsync(save, CancellationToken.None));
    }

    private async Task<List<(string Path, string Package, byte[] Bytes)>> Export()
    {
        var result = await GamePackageExporter.ExportDirectoryAsync("story-project", "Story", Source, Installed,
            _types, BuiltinGalleryTypes.CreateCatalog(_types), _catalog);
        Assert.That(result.Success, Is.True, result.Error);
        return Directory.GetFiles(Path.Combine(Installed, "StoryPatches"), "*.pak", SearchOption.AllDirectories).Select(path =>
        {
            var bytes = File.ReadAllBytes(path);
            using var archive = Archive.Deserialize("assets", bytes, _types);
            return (path, archive.Descriptor.PackageId, bytes);
        }).ToList();
    }
    private Task<GalNet.GameHost.Content.GameContent> Load() => new InstalledGameContentProvider(Installed, _types, BuiltinGalleryTypes.CreateCatalog(_types)).LoadAsync();
    private string[] Ids(string path) { using var archive = Archive.Deserialize("assets", File.ReadAllBytes(path), _types); return archive.AssetIds.ToArray(); }
    private static void SetInstalled(List<(string Path, string Package, byte[] Bytes)> files, int mask)
    {
        foreach (var file in files)
            if ((mask & (file.Package == "A" ? 1 : 2)) != 0) File.WriteAllBytes(file.Path, file.Bytes);
            else if (File.Exists(file.Path)) File.Delete(file.Path);
    }
    private void Group(string id, params string[] images) => Put("Graph/groups/" + id + ".rawgalgroup", JsonSerializer.Serialize(new
    {
        version = 2, kind = "Raw", entries = images.Select((image, index) => new { id = "e" + index, type = "layer.show", parameters = new { assetId = image } })
    }));
    private void Put(string path, string text) { var file = Path.Combine(Source, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
}
