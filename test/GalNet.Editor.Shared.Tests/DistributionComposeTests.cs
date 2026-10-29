using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Assets;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Serialization;
using GalNet.Editor.Shared.Services;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Editor.Shared;

public sealed class DistributionComposeTests
{
    private string _root = null!;
    private string Source => Path.Combine(_root, "source");
    private string Runtime => Path.Combine(_root, "runtime");
    private string Content => Path.Combine(_root, "content");
    private string Output => Path.Combine(_root, "output", "game.zip");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [SetUp]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "GalNet-compose-" + Guid.NewGuid().ToString("N"));
        var repo = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(repo.FullName, "GalNet.slnx"))) repo = repo.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        using var contracts = ProjectContractSource.Prepare(Path.Combine(repo.FullName, "src/Editor/GalNet.Editor.DefaultContracts/bin", configuration, "net10.0"));
        Directory.CreateDirectory(Path.Combine(Source, "Contracts"));
        Directory.CreateDirectory(Path.Combine(Runtime, "lib"));
        Directory.CreateDirectory(Path.Combine(Runtime, "docs"));
        Directory.CreateDirectory(Path.Combine(Runtime, "pak"));
        foreach (var file in Directory.GetFiles(contracts.DirectoryPath))
        {
            File.Copy(file, Path.Combine(Source, "Contracts", Path.GetFileName(file)));
            File.Copy(file, Path.Combine(Runtime, "lib", Path.GetFileName(file)));
        }
        Directory.CreateDirectory(Path.Combine(Source, "Graph/groups"));
        File.WriteAllText(Path.Combine(Source, "Graph/graph.json"), """{"version":2,"name":"Compose","rootNodeId":"main","nodes":[{"id":"main","name":"Main","type":"Group","file":"groups/main.rawgalgroup"}],"edges":[],"playerVariables":[],"saveVariables":[]}""");
        File.WriteAllText(Path.Combine(Source, "Graph/groups/main.rawgalgroup"), """{"version":2,"kind":"Raw","entries":[]}""");
        File.WriteAllText(Path.Combine(Source, "settings.json"), """{"version":1,"sections":{}}""");
        File.Copy(Environment.ProcessPath!, Path.Combine(Runtime, "Player.exe"));
        File.WriteAllText(Path.Combine(Runtime, ApplicationPackageLayout.FileName), JsonSerializer.Serialize(new ApplicationPackageLayout
        {
            RuntimeIdentifier = "win-x64", EntryPoint = "Player.exe", ManagedDirectory = "lib", ContractsDirectory = "lib", ContentDirectory = "pak"
        }, JsonOptions));
        await WriteRuntimeManifest();
        using var schema = ProjectSchemaContext.Load(Source);
        var types = BuiltinResourceTypes.CreateCatalog();
        var directory = await GamePackageExporter.ExportDirectoryAsync("compose-project", "Compose", Source, Content,
            types, BuiltinGalleryTypes.CreateCatalog(types), schema.Catalog, schema.Configurations);
        Assert.That(directory.Success, Is.True, directory.Error);
        var archive = await GamePackageExporter.ExportAsync("compose-project", "Compose", Source, Path.Combine(_root, "archives"),
            types, BuiltinGalleryTypes.CreateCatalog(types), schema.Catalog, schema.Configurations);
        Assert.That(archive.Success, Is.True, archive.Error);
    }

    [TearDown] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ComposesBothInputFormsAndRebuildsTheFinalHashManifest(bool runtimeZip, bool contentZip)
    {
        var runtime = Runtime;
        if (runtimeZip) { runtime = Path.Combine(_root, "runtime.zip"); ZipFile.CreateFromDirectory(Runtime, runtime); }
        var content = contentZip ? Path.Combine(_root, "archives/Compose.galpak") : Content;
        var result = await GameDistributionComposer.ComposeAsync(runtime, content, Output);
        Assert.That(result.ProjectId, Is.EqualTo("compose-project"));
        Assert.That(result.Sha256, Is.EqualTo(CryptoHelper.HashSHA256(File.ReadAllBytes(Output))));
        var extracted = Path.Combine(_root, "extracted"); ZipFile.ExtractToDirectory(Output, extracted);
        Assert.That(ApplicationPackageLayout.FindApplicationRoot(Path.Combine(extracted, "lib")), Is.EqualTo(extracted));
        var game = PackedGameDocuments.ReadGame(File.ReadAllBytes(Path.Combine(extracted, "pak/game.pak")));
        Assert.That(game.Manifest.ContractFiles.Count, Is.GreaterThanOrEqualTo(3));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(extracted, GameDistributionComposer.ManifestPath)));
        Assert.That(manifest.RootElement.GetProperty("package").GetString(), Is.EqualTo("Sample-Game"));
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
            Assert.That(CryptoHelper.HashSHA256(File.ReadAllBytes(Path.Combine(extracted, entry.GetProperty("path").GetString()!))),
                Is.EqualTo(entry.GetProperty("sha256").GetString()));
        Assert.That(File.Exists(Path.Combine(Runtime, "pak/game.pak")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(extracted, "pak/ResourcePatches")), Is.True);
    }

    [TestCase("hash")]
    [TestCase("contract")]
    [TestCase("undeclared-runtime")]
    [TestCase("undeclared-content")]
    [TestCase("overlap")]
    public async Task RejectsInvalidInputsWithoutPublishingOrChangingContent(string change)
    {
        var original = File.ReadAllBytes(Path.Combine(Content, "game.pak"));
        if (change is "hash" or "contract")
        {
            File.AppendAllText(Path.Combine(Runtime, "lib/GalNet.Project.Contracts.dll"), "changed");
            if (change == "contract") await WriteRuntimeManifest();
        }
        else if (change == "undeclared-runtime") File.WriteAllText(Path.Combine(Runtime, "unexpected.txt"), "extra");
        else if (change == "undeclared-content") File.WriteAllText(Path.Combine(Content, "settings.json"), "{}");
        else
        {
            var layout = JsonNode.Parse(File.ReadAllText(Path.Combine(Runtime, ApplicationPackageLayout.FileName)))!;
            layout["contentDirectory"] = "lib"; File.WriteAllText(Path.Combine(Runtime, ApplicationPackageLayout.FileName), layout.ToJsonString());
            await WriteRuntimeManifest();
        }
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Output));
        Assert.That(File.Exists(Output), Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(Content, "game.pak")), Is.EqualTo(original));
        Assert.That(Directory.GetDirectories(Path.GetDirectoryName(Output)!, ".compose-*"), Is.Empty);
    }

    [TestCase("../escape")]
    [TestCase("Player.exe")]
    [TestCase("link")]
    public void RejectsUnsafeRuntimeArchives(string extra)
    {
        var archive = Path.Combine(_root, "runtime.zip"); ZipFile.CreateFromDirectory(Runtime, archive);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            var entry = zip.CreateEntry(extra);
            if (extra == "link") entry.ExternalAttributes = (0xa1ff << 16);
            using var writer = new StreamWriter(entry.Open()); writer.Write("outside");
        }
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(archive, Content, Output));
        Assert.That(File.Exists(Path.Combine(_root, "escape")), Is.False);
        Assert.That(File.Exists(Output), Is.False);
    }

    [Test]
    public void RefusesContentWithoutADeclaredContractIdentity()
    {
        var path = Path.Combine(Content, PackedGameDocuments.GameFileName);
        var packed = PackedGameDocuments.ReadGame(File.ReadAllBytes(path));
        var documents = packed.Documents.Documents.ToDictionary();
        documents[GameContentManifest.FileName] = JsonSerializer.SerializeToUtf8Bytes(packed.Manifest with { ContractFiles = [] }, JsonOptions);
        File.WriteAllBytes(path, PackedGameDocuments.Build(packed.Manifest.ProjectId, packed.Manifest.BuildId, documents));
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Output));
        Assert.That(File.Exists(Output), Is.False);
    }

    [Test]
    public void RefusesExistingOrNestedOutputAndCleansCanceledStages()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Output)!); File.WriteAllText(Output, "keep");
        Assert.ThrowsAsync<IOException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Output));
        Assert.That(File.ReadAllText(Output), Is.EqualTo("keep"));
        Assert.ThrowsAsync<InvalidDataException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Path.Combine(Runtime, "game.zip")));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => GameDistributionComposer.ComposeAsync(Runtime, Content, Path.Combine(_root, "canceled.zip"), canceled.Token));
        Assert.That(Directory.GetDirectories(_root, ".compose-*"), Is.Empty);
    }

    private Task WriteRuntimeManifest()
    {
        var files = Directory.GetFiles(Runtime, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(Runtime, path).Replace('\\', '/') != GameDistributionComposer.ManifestPath)
            .Select(path => new GalpakFileEntry(Path.GetRelativePath(Runtime, path).Replace('\\', '/'),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), new FileInfo(path).Length));
        return File.WriteAllTextAsync(Path.Combine(Runtime, GameDistributionComposer.ManifestPath), JsonSerializer.Serialize(new
        { schemaVersion = 1, runtime = "win-x64", package = "Sample", files }, JsonOptions));
    }
}
