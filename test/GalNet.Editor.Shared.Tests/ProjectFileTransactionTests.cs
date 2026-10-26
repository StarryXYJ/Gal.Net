using GalNet.Contracts.Builtins.Assets;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class ProjectFileTransactionTests
{
    private string _root = null!;
    [SetUp] public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-files-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown] public void TearDown() => Directory.Delete(_root, true);

    [TestCase(false)]
    [TestCase(true)]
    public void InterruptedFilePublicationCanRecoverOrProtectExternalContent(bool externalChange)
    {
        var plan = new ProjectSavePlan(new Dictionary<string, byte[]> { ["notes/a.bin"] = [1], ["notes/b.bin"] = [2] }, []);
        Assert.Throws<ProjectRepairRequiredException>(() => new FileFault().Commit(_root, plan));
        var manifest = Directory.GetFiles(Path.Combine(_root, ".galnet/editor/transactions"), "manifest.json", SearchOption.AllDirectories).Single();
        Assert.That(File.Exists(Path.Combine(_root, "notes/a.bin")), Is.True);
        if (externalChange)
        {
            File.WriteAllBytes(Path.Combine(_root, "notes/a.bin"), [9]);
            Assert.Throws<ProjectRepairRequiredException>(() => new AuthorFileTransaction().Recover(_root));
            Assert.That(File.ReadAllBytes(Path.Combine(_root, "notes/a.bin")), Is.EqualTo(new byte[] { 9 }));
            Assert.That(File.Exists(manifest), Is.True);
        }
        else
        {
            new AuthorFileTransaction().Recover(_root);
            Assert.That(Directory.Exists(Path.Combine(_root, "notes")), Is.False);
            Assert.That(File.Exists(manifest), Is.False);
        }
    }

    [Test]
    public void InterruptedCaseDirectoryRenameRestoresTheManifestTemporaryObject()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows case rename recovery");
        var source = Path.Combine(_root, "Assets/folder"); Directory.CreateDirectory(source); File.WriteAllBytes(Path.Combine(source, "a.bin"), [7]);
        var hash = AuthorFileTransaction.ReadHash(Path.Combine(source, "a.bin"));
        var directory = Path.Combine(_root, ".galnet/editor/transactions/interrupted"); Directory.CreateDirectory(directory);
        Directory.Move(source, Path.Combine(directory, "case-0"));
        var manifest = new AuthorTransactionManifest { Status = "publishing", Files = [new("Assets/folder/a.bin", hash, hash, false)], CaseRenames = [new("Assets/folder", "Assets/FOLDER", ".galnet/editor/transactions/interrupted/case-0")] };
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        new AuthorFileTransaction().Recover(_root);
        Assert.That(File.ReadAllBytes(Path.Combine(source, "a.bin")), Is.EqualTo(new byte[] { 7 }));
        Assert.That(Directory.EnumerateDirectories(Path.Combine(_root, "Assets")).Select(Path.GetFileName), Is.EqualTo(new[] { "folder" }));
    }

    [Test]
    public async Task CliAssetCommandsShareMetadataAndTransactionRulesWithoutRetainingUndo()
    {
        await EditorProjectCreator.CreateAsync(new(_root, "CLI"));
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var executor = new AssetFileCommandExecutor(catalog);
        var external = Path.Combine(_root, "external.png"); File.WriteAllBytes(external, [1, 2]);
        Assert.That((await executor.ExecuteAsync(_root, new ImportAssetsCommand([external]))).Success, Is.True);
        var codec = new AssetMetaCodec(BuiltinResourceTypes.CreateCatalog());
        var id = codec.Deserialize(File.ReadAllText(Path.Combine(_root, "Assets/external.png.meta"))).Id;
        Assert.That((await executor.ExecuteAsync(_root, new CreateAssetDirectoryCommand("", "folder"))).Success, Is.True);
        Assert.That((await executor.ExecuteAsync(_root, new MoveAssetCommand("external.png", "folder"))).Success, Is.True);
        var meta = codec.Deserialize(File.ReadAllText(Path.Combine(_root, "Assets/folder/external.png.meta")));
        Assert.That(meta.Id, Is.EqualTo(id)); Assert.That(meta.Path, Is.EqualTo("folder/external.png"));
        Assert.That(Directory.GetDirectories(Path.Combine(_root, ".galnet/editor/history")), Is.Empty);
        var invalid = await executor.ExecuteAsync(_root, new RenameAssetCommand("folder/external.png", "../escape.png"));
        Assert.That(invalid.Success, Is.False); Assert.That(File.Exists(Path.Combine(_root, "Assets/folder/external.png")), Is.True);
        Assert.DoesNotThrow(() => JsonSerializer.Serialize(invalid), "Error details are not part of the CLI JSON contract.");
    }

    [Test]
    public async Task RetainedFileCleanupKeepsLiveOperationsAndReleasesAbandonedMaterial()
    {
        await EditorProjectCreator.CreateAsync(new(_root, "Lease"));
        var persistence = new DirectProjectPersistence(_root, new EditorDocumentRepository(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())));
        var document = persistence.Load();
        using var operation = new ProjectFileOperation(_root, new(ProjectFileAction.CreateFile, [], "notes/a.txt", Content: [1]), new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
        await operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare));
        var root = Path.Combine(_root, ".galnet/editor/history");
        ProjectFileOperation.CleanupOrphans(_root);
        Assert.That(Directory.GetDirectories(root), Has.Length.EqualTo(1));
        var abandoned = Path.Combine(root, "abandoned"); Directory.CreateDirectory(abandoned); File.WriteAllText(Path.Combine(abandoned, "owner.lock"), ""); File.WriteAllBytes(Path.Combine(abandoned, "payload"), [8]);
        ProjectFileOperation.CleanupOrphans(_root);
        Assert.That(Directory.Exists(abandoned), Is.False); Assert.That(Directory.GetDirectories(root), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task LinkedDestinationsAndDuplicateBatchTargetsCannotEscapeOrOverwrite()
    {
        await EditorProjectCreator.CreateAsync(new(_root, "Paths"));
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var persistence = new DirectProjectPersistence(_root, new EditorDocumentRepository(catalog)); var document = persistence.Load();
        Directory.CreateDirectory(Path.Combine(_root, "first")); Directory.CreateDirectory(Path.Combine(_root, "second")); Directory.CreateDirectory(Path.Combine(_root, "target"));
        File.WriteAllBytes(Path.Combine(_root, "first/a.bin"), [1]); File.WriteAllBytes(Path.Combine(_root, "second/a.bin"), [2]);
        using var operation = new ProjectFileOperation(_root, new(ProjectFileAction.Move, ["first/a.bin", "second/a.bin"], "target"), catalog);
        Assert.ThrowsAsync<EditorOperationException>(() => operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare)));
        Assert.That(Directory.GetFiles(Path.Combine(_root, "target")), Is.Empty);
        var link = Path.Combine(_root, "link");
        try { Directory.CreateSymbolicLink(link, Path.Combine(_root, "target")); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException) { Assert.Ignore("Directory links unavailable: " + error.Message); }
        using var linked = new ProjectFileOperation(_root, new(ProjectFileAction.Move, ["first/a.bin"], "link"), catalog);
        Assert.ThrowsAsync<InvalidDataException>(() => linked.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare)));
        Assert.That(File.Exists(Path.Combine(_root, "first/a.bin")), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidRecoveryLocationsKeepMaterialsWithoutTouchingProtectedContent(bool caseTemporary)
    {
        var directory = Path.Combine(_root, ".galnet/editor/transactions/invalid"); Directory.CreateDirectory(directory);
        var manifest = new AuthorTransactionManifest { Status = "publishing" };
        if (caseTemporary) manifest.CaseRenames.Add(new("Assets/a", "Assets/A", ".galnet/editor/transactions/another/case-0"));
        else manifest.Directories.Add(new("Contracts/private", false, true));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Throws<ProjectRepairRequiredException>(() => new AuthorFileTransaction().Recover(_root));
        Assert.That(Directory.Exists(Path.Combine(_root, "Contracts")), Is.False);
        Assert.That(File.Exists(Path.Combine(directory, "manifest.json")), Is.True);
    }

    [Test]
    public async Task InvalidOptionalMetadataCannotBeSilentlyReplacedByAuthoring()
    {
        await EditorProjectCreator.CreateAsync(new(_root, "Metadata"));
        File.WriteAllBytes(Path.Combine(_root, "Assets/a.png"), [1]);
        var json = """{"id":"asset-a","type":"sprite","path":"a.png","gallery":"invalid","custom":{"x":17}}""";
        File.WriteAllText(Path.Combine(_root, "Assets/a.png.meta"), json);
        var executor = new AssetFileCommandExecutor(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
        var result = await executor.ExecuteAsync(_root, new RenameAssetCommand("a.png", "b.png"));
        Assert.That(result.Success, Is.False);
        Assert.That(File.ReadAllText(Path.Combine(_root, "Assets/a.png.meta")), Is.EqualTo(json));
        Assert.That(File.Exists(Path.Combine(_root, "Assets/a.png")), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, "Assets/b.png")), Is.False);
    }

    [Test]
    public async Task DeclaredResourceReferencesCannotBeBypassedAsStringCandidates()
    {
        await EditorProjectCreator.CreateAsync(new(_root, "References"));
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var persistence = new DirectProjectPersistence(_root, new EditorDocumentRepository(catalog)); var document = persistence.Load();
        File.WriteAllBytes(Path.Combine(_root, "Assets/a.png"), [1]);
        File.WriteAllText(Path.Combine(_root, "Assets/a.png.meta"), """{"id":"asset-a","type":"sprite","path":"a.png"}""");
        var definition = catalog.Definitions.First(item => item.DynamicParameters?.Values.Any(field => field.Editor?.EditorId?.Contains("asset", StringComparison.OrdinalIgnoreCase) == true) == true);
        var parameter = definition.DynamicParameters!.First(pair => pair.Value.Editor?.EditorId?.Contains("asset", StringComparison.OrdinalIgnoreCase) == true).Key;
        document.GroupEntries.Values.First().Add(new() { StableId = "reference-entry", Type = definition.Type, Parameters = new() { [parameter] = JsonSerializer.SerializeToElement("asset-a") } });
        using var operation = new ProjectFileOperation(_root, new(ProjectFileAction.Delete, ["Assets/a.png"], AllowCandidateReferences: true), catalog);
        Assert.That(operation.Analyze(document, document).References.Single().Certain, Is.True);
        var error = Assert.ThrowsAsync<EditorOperationException>(() => operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare)));
        Assert.That(error!.Code, Is.EqualTo("files.referenced"));
        Assert.That(File.Exists(Path.Combine(_root, "Assets/a.png")), Is.True);
    }

    [Test]
    public async Task MetadataPatchRejectsDirectoriesWithoutDeletingThem()
    {
        await EditorProjectCreator.CreateAsync(new(_root, "Directory metadata"));
        var directory = Path.Combine(_root, "Assets/empty"); Directory.CreateDirectory(directory);
        var executor = new AssetFileCommandExecutor(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
        var result = await executor.ExecuteAsync(_root, new PatchAssetMetadataCommand("empty", Compression: "none"));
        Assert.That(result.Success, Is.False);
        Assert.That(Directory.Exists(directory), Is.True);
        Assert.That(File.Exists(directory + ".meta"), Is.False);
    }

    private sealed class FileFault : AuthorFileTransaction
    {
        private int _published;
        protected override void Publish(string path, string? stagedFile) { if (++_published == 2) throw new IOException("Injected publication failure"); base.Publish(path, stagedFile); }
        protected override void Restore(string path, string? backupFile) => throw new IOException("Injected compensation failure");
    }
}
