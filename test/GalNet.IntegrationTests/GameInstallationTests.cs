using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Serialization;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GalNet.GameHost.Content;
using GalNet.Storage.FileSystem;
using AssetCompressionMode = GalNet.Core.Assets.CompressionMode;

namespace GalNet.Assets.Tests;

public sealed class GameInstallationTests
{
    public sealed class HostOptions { public double SubtitleScale { get; set; } = 1; public int Slots { get; set; } = 60; }

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
            var resourceTypes = BuiltinResourceTypes.CreateCatalog();
            var galleryTypes = BuiltinGalleryTypes.CreateCatalog(resourceTypes);
            var section = new ConfigurationSection<HostOptions>("host.options", [new("subtitleScale", new ValueDescriptor(typeof(double), allowNull: false)), new("slots", new ValueDescriptor(typeof(int), allowNull: false))]);
            var configurations = new ProjectConfigurationSchema([DefaultProjectConfiguration.Section, section]);
            var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
            await File.WriteAllBytesAsync(Path.Combine(project, "Assets", "hero.png"), "hero"u8.ToArray());
            await File.WriteAllTextAsync(Path.Combine(project, "Assets", "hero.png.meta"), """{"id":"hero","type":"sprite","path":"hero.png","filter":"bilinear","compress":"none"}""");
            await File.WriteAllTextAsync(Path.Combine(project, "Graph", "graph.json"), """{"version":2,"name":"export-test","rootNodeId":"","nodes":[],"edges":[],"playerVariables":[],"saveVariables":[]}""");
            await File.WriteAllTextAsync(Path.Combine(project, "settings.json"), """
                { "version": 1, "sections": { "galnet.project": { "version": 2, "data": { "audioBuses": [{ "id": "environment", "name": "Environment" }], "audioTracks": [ { "id": "ambient", "busId": "environment", "preserveDuringCatchUp": true, "defaultGain": 0.3, "maxVoices": 2, "overflowPolicy": "StopOldest" } ] } } } }
                """);
            var edit = await new ProjectEditService(project, catalog, configurations).ExecuteJsonAsync(
                """{"commandId":"project.configuration.patch","sectionId":"host.options","values":{"subtitleScale":1.5,"slots":3}}""");
            Assert.That(edit.Success, Is.True, JsonSerializer.Serialize(edit.Diagnostics));

            var development = await GameInstallation.OpenAsync(project);
            Assert.That(development.IsPackaged, Is.False);
            var developmentContent = await development.CreateContentProvider(resourceTypes, galleryTypes).LoadAsync();
            Assert.That(developmentContent.Graph.Name, Is.EqualTo("export-test"));
            Assert.That(developmentContent.Configuration.ReadDefaultOptions().AudioTracks.Single().Id, Is.EqualTo("ambient"));
            using (var sourceAssets = new AssetManager(development.CreateAssetProviders(resourceTypes)))
                Assert.That((await sourceAssets.GetFileAsync("hero"))?.Path, Is.EqualTo("hero.png"));

            Directory.CreateDirectory(Path.Combine(project, "I18n", "External", "en-US"));
            await File.WriteAllTextAsync(Path.Combine(project, "I18n", "External", "en-US", "private.json"), "{}");
            var export = await GamePackageExporter.ExportAsync("project", "Sample", project, output, resourceTypes, galleryTypes, catalog, configurations);
            Assert.That(export.Success, Is.True, export.Error);
            using (var packageZip = ZipFile.OpenRead(export.PackagePath!))
                Assert.That(packageZip.Entries.Any(entry => entry.FullName.StartsWith("I18n/External/", StringComparison.OrdinalIgnoreCase)), Is.False);

            var installation = await GameInstallation.OpenAsync(export.PackagePath!);
            Assert.That(File.Exists(Path.Combine(installation.RootDirectory, "gallery.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(installation.RootDirectory, "Graph", "graph.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(installation.RootDirectory, GameContentManifest.FileName)), Is.True);
            Assert.That(File.Exists(Path.Combine(installation.RootDirectory, "Assets", "Paks", "000-base.pak")), Is.True);
            var installedContent = await installation.CreateContentProvider(resourceTypes, galleryTypes,
                new GameContentLoadOptions { Configurations = configurations }).LoadAsync();
            Assert.That(installedContent.Configuration.Read(section).SubtitleScale, Is.EqualTo(1.5));
            var host = new GalNet.Avalonia.GameHost.AvaloniaGameHostOptions
            {
                CreateContentCatalogs = () => (resourceTypes, galleryTypes),
                ConfigureExecution = (_, _, _, _) => { },
                SelectProjectOptions = configuration => new() { SaveSlotCount = configuration.Read(section).Slots }
            };
            using var player = await FilePlayerSession.OpenAsync(Path.Combine(root, "profile"), installedContent.Gallery,
                host.SelectProjectOptions(installedContent.Configuration).SaveSlotCount);
            Assert.That(await player.ListSlotsAsync(CancellationToken.None), Has.Count.EqualTo(3));
            Assert.That(installedContent.Graph.Name, Is.EqualTo("export-test"));
            Assert.That(installedContent.Configuration.ReadDefaultOptions().AudioBuses.Single().Id, Is.EqualTo("environment"));
            Assert.That(installedContent.Configuration.ReadDefaultOptions().AudioTracks.Single().BusId, Is.EqualTo("environment"));
            Assert.That(installedContent.Configuration.ReadDefaultOptions().AudioTracks.Single().PreserveDuringCatchUp, Is.True);
            Assert.That(installedContent.Configuration.ReadDefaultOptions().AudioTracks.Single().DefaultGain, Is.EqualTo(.3f));
            Assert.That(installedContent.Configuration.ReadDefaultOptions().AudioTracks.Single().OverflowPolicy, Is.EqualTo(GalNet.Contracts.Builtins.Audio.AudioOverflowPolicy.StopOldest));

            using var assets = new AssetManager(installation.CreateAssetProviders(resourceTypes));
            Assert.That(await assets.GetFileAsync("hero"), Is.Null, "Unused source resources are excluded from the installed build.");
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
            var galleryTypes = BuiltinGalleryTypes.CreateCatalog(types);
            var baseAssets = PakBuilder.Build("assets", [Asset("hero", "base")], types, AssetCompressionMode.None);
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
            Assert.That((await installation.CreateContentProvider(types, galleryTypes).LoadAsync()).Graph.Name, Is.EqualTo("package-test"));

            var resourcePaks = Path.Combine(installation.RootDirectory, "Assets", "Paks");
            await File.WriteAllBytesAsync(Path.Combine(resourcePaks, "100-extra.pak"), PakBuilder.Build("assets", [Asset("hero", "patch")], types, AssetCompressionMode.None));

            using var manager = new AssetManager(installation.CreateAssetProviders(types));
            using var handle = await manager.AcquireAsync<string>("hero");
            Assert.That(handle?.Value, Is.EqualTo("base"), "An undeclared base PAK cannot act as a resource override.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ReinstallAndRejectedUpdatePreserveExternalTranslations()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-external-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var package = Path.Combine(root, "sample.galpak");
            var files = BuildContentFiles(BuiltinResourceTypes.CreateCatalog());
            await CreatePackageAsync(package, files);
            var installed = await GalpakInstaller.InstallAsync(package);
            var external = Path.Combine(installed, "I18n", "External", "en-US");
            Assert.That(Directory.Exists(Path.GetDirectoryName(external)), Is.True);
            Directory.CreateDirectory(external);
            var translation = Path.Combine(external, "third-party.json");
            await File.WriteAllTextAsync(translation, """{"line":"preserved"}""");
            Assert.That(await GalpakInstaller.InstallAsync(package), Is.EqualTo(installed));
            await CreatePackageAsync(package, files.Append(("settings.json", """{"version":1,"sections":{}}"""u8.ToArray())).ToArray());
            Assert.ThrowsAsync<InvalidOperationException>(() => GalpakInstaller.InstallAsync(package));
            Assert.That(await File.ReadAllTextAsync(translation), Does.Contain("preserved"));
        }
        finally { Directory.Delete(root, true); }
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
        var graphMeta = new DependentAssetMeta { Id = StoryResourceIds.Graph, TypeId = "graph", Path = "Story/main.galgraph", DependenciesComplete = true };
        var graph = new GameFile(graphMeta.Id, graphMeta.Path, graphMeta.TypeId, graphMeta,
            System.Text.Encoding.UTF8.GetBytes("""{"id":"galnet:graph:main","name":"package-test","rootNodeId":"","nodes":[],"edges":[]}"""));
        return [
            (GameContentManifest.FileName, JsonSerializer.SerializeToUtf8Bytes(new GameContentManifest(), JsonSerializerOptions.Web)),
            ("Assets/Paks/010-story.pak", PakBuilder.Build("assets", [graph], types)),
            ("gallery.json", JsonSerializer.SerializeToUtf8Bytes(gallery))
        ];
    }

    private static async Task CreatePackageAsync(string path, IReadOnlyList<(string Path, byte[] Data)> files)
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        var data = files.Where(file => file.Path != GameContentManifest.FileName).ToList();
        var paks = data.Select((file, index) => (file, index)).Where(item => item.file.Path.EndsWith(".pak", StringComparison.Ordinal)).ToArray();
        var buildId = new string('a', 64);
        for (var part = 0; part < paks.Length; part++)
        {
            var item = paks[part];
            using var source = Archive.Deserialize("assets", item.file.Data, types);
            data[item.index] = (item.file.Path, PakBuilder.Build("assets", source.AssetIds.Select(id => source.GetAsset(id)!).ToArray(), types,
                descriptor: new() { ProjectId = "test", BuildId = buildId, PartIndex = part, PartCount = paks.Length }));
        }
        data.Add((GameContentManifest.FileName, JsonSerializer.SerializeToUtf8Bytes(new GameContentManifest
        {
            ProjectId = "test",
            BuildId = buildId,
            BasePaks = data.Where(file => file.Path.EndsWith(".pak", StringComparison.Ordinal))
            .Select(file => new GalpakFileEntry(file.Path, Hash(file.Data), file.Data.LongLength)).ToList()
        }, JsonSerializerOptions.Web)));
        files = data;
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
