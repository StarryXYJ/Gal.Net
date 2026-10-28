using System.IO.Compression;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class ImageVariantsResourceTests
{
    private string _root = null!;
    private readonly IEntryCatalog _catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
    [SetUp]
    public async Task SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "galnet-variant-project-" + Guid.NewGuid().ToString("N"));
        await EditorProjectCreator.CreateAsync(new(_root, "Variants"));
        PutAsset("alice/idle_coat.png", "sprite", "image");
        PutAsset("alice/weap.png", "sprite", "image");
        PutAsset("alice.variants", "image-variants", """{"folder":"alice","default":"idle_coat"}""");
    }
    [TearDown] public void TearDown() => Directory.Delete(_root, true);

    private void PutAsset(string path, string type, string content)
    {
        var full = Path.Combine(_root, "Assets", path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        File.WriteAllText(full + ".meta", JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), type }));
    }

    private string Id(string path) => new AssetMetaCodec(BuiltinResourceTypes.CreateCatalog()).Deserialize(File.ReadAllText(Path.Combine(_root, "Assets", path + ".meta")), path).Id;

    private async Task<byte[]> Compiled(string descriptor, IResourceTypeCatalog? resourceTypes = null)
    {
        using var provider = new LocalFileProvider(Path.Combine(_root, "Assets"), resourceTypes ?? BuiltinResourceTypes.CreateCatalog());
        using var archive = await provider.OpenArchiveAsync("assets");
        return archive.GetAssetByPath(descriptor)!.ReadAllBytes();
    }

    [TestCase("alice", "Assets/new", true)]
    [TestCase("alice.variants", "Assets/new", true)]
    [TestCase("alice", "Assets/ALICE", false)]
    public async Task Moving_images_or_descriptor_preserves_identity_and_undo_redo_restores_relations(string source, string destination, bool targetDirectory)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Assets/new"));
        var original = await Compiled("alice.variants");
        var spriteId = Id("alice/idle_coat.png");
        var persistence = new DirectProjectPersistence(_root, new EditorDocumentRepository(_catalog));
        var document = persistence.Load();
        using var operation = new ProjectFileOperation(_root, new(ProjectFileAction.Move, ["Assets/" + source], destination, targetDirectory), _catalog);
        var descriptor = source == "alice.variants" ? "new/alice.variants" : "alice.variants";
        await operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare));
        Assert.That(await Compiled(descriptor), Is.EqualTo(original));
        var image = source == "alice.variants" ? "alice/idle_coat.png" : targetDirectory ? "new/alice/idle_coat.png" : "ALICE/idle_coat.png";
        Assert.That(Id(image), Is.EqualTo(spriteId));
        await operation.ApplyAsync(false, prepare => persistence.PublishStructureAsync(document, prepare));
        Assert.That(await Compiled("alice.variants"), Is.EqualTo(original));
        await operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare));
        Assert.That(await Compiled(descriptor), Is.EqualTo(original));
        // A new provider simulates closing/reopening the resource scope after publication.
        Assert.That(ImageVariantsCodec.Decode(await Compiled(descriptor)).Default.AssetId, Is.EqualTo(spriteId));
    }

    [Test]
    public async Task Copying_a_complete_directory_generates_new_ids_and_points_to_its_own_images()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Assets/pack"));
        Directory.Move(Path.Combine(_root, "Assets/alice"), Path.Combine(_root, "Assets/pack/alice"));
        File.Move(Path.Combine(_root, "Assets/alice.variants"), Path.Combine(_root, "Assets/pack/alice.variants"));
        File.Move(Path.Combine(_root, "Assets/alice.variants.meta"), Path.Combine(_root, "Assets/pack/alice.variants.meta"));
        Directory.CreateDirectory(Path.Combine(_root, "Assets/copies"));
        var persistence = new DirectProjectPersistence(_root, new EditorDocumentRepository(_catalog));
        var document = persistence.Load();
        using var operation = new ProjectFileOperation(_root, new(ProjectFileAction.Copy, ["Assets/pack"], "Assets/copies"), _catalog);
        await operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare));
        var compiled = ImageVariantsCodec.Decode(await Compiled("copies/pack/alice.variants"));
        Assert.That(compiled.Default.AssetId, Is.EqualTo(Id("copies/pack/alice/idle_coat.png")));
        Assert.That(compiled.Default.AssetId, Is.Not.EqualTo(Id("pack/alice/idle_coat.png")));
        Assert.That(Id("copies/pack/alice.variants"), Is.Not.EqualTo(Id("pack/alice.variants")));
        await operation.ApplyAsync(false, prepare => persistence.PublishStructureAsync(document, prepare));
        Assert.That(Directory.Exists(Path.Combine(_root, "Assets/copies/pack")), Is.False);
        await operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare));
        Assert.That(ImageVariantsCodec.Decode(await Compiled("copies/pack/alice.variants")).Default.AssetId, Is.EqualTo(compiled.Default.AssetId));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Renaming_or_deleting_the_default_image_is_rejected_before_writing(bool delete)
    {
        var before = await Compiled("alice.variants");
        var persistence = new DirectProjectPersistence(_root, new EditorDocumentRepository(_catalog));
        var document = persistence.Load();
        using var operation = new ProjectFileOperation(_root, delete
            ? new(ProjectFileAction.Delete, ["Assets/alice/idle_coat.png"])
            : new(ProjectFileAction.Move, ["Assets/alice/idle_coat.png"], "Assets/alice/new.png", false), _catalog);
        Assert.ThrowsAsync<InvalidDataException>(() => operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare)));
        Assert.That(await Compiled("alice.variants"), Is.EqualTo(before));
        Assert.That(File.Exists(Path.Combine(_root, "Assets/alice/idle_coat.png")), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, "Assets/alice/new.png")), Is.False);
    }

    [Test]
    public async Task Importing_a_folder_creates_sidecars_in_one_transaction_and_can_be_undone()
    {
        var external = Path.Combine(_root, "external");
        Directory.CreateDirectory(Path.Combine(external, "images"));
        File.WriteAllText(Path.Combine(external, "images/idle.png"), "image");
        File.WriteAllText(Path.Combine(external, "hero.variants"), """{"folder":"images","default":"idle"}""");
        var persistence = new DirectProjectPersistence(_root, new EditorDocumentRepository(_catalog));
        var document = persistence.Load();
        using var operation = new ProjectFileOperation(_root, new(ProjectFileAction.Import, [external], "Assets"), _catalog);
        await operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare));
        Assert.That(ImageVariantsCodec.Decode(await Compiled("external/hero.variants")).Default.AssetId, Is.EqualTo(Id("external/images/idle.png")));
        Assert.That(File.Exists(Path.Combine(external, "hero.variants.meta")), Is.False);
        await operation.ApplyAsync(false, prepare => persistence.PublishStructureAsync(document, prepare));
        Assert.That(Directory.Exists(Path.Combine(_root, "Assets/external")), Is.False);
    }

    [Test]
    public void Validation_and_build_reject_bad_definition_and_preserve_previous_output()
    {
        File.WriteAllText(Path.Combine(_root, "Assets/alice.variants"), """{"folder":"alice","default":"missing"}""");
        File.WriteAllText(Path.Combine(_root, "Output/sentinel"), "old-output");
        var validation = new ProjectValidationService(_root).Validate(catalog: _catalog);
        Assert.That(validation.Success, Is.False);
        Assert.That(validation.Diagnostics.Any(item => item.Code == "variants.invalid"), Is.True);
        Assert.ThrowsAsync<InvalidDataException>(() => new ProjectContentBuilder(_catalog).BuildAsync(_root));
        Assert.That(File.ReadAllText(Path.Combine(_root, "Output/sentinel")), Is.EqualTo("old-output"));
    }

    [Test]
    public async Task Explicit_metadata_keeps_descriptor_semantics_after_changing_its_extension()
    {
        var persistence = new DirectProjectPersistence(_root, new EditorDocumentRepository(_catalog));
        var document = persistence.Load();
        using var operation = new ProjectFileOperation(_root,
            new(ProjectFileAction.Move, ["Assets/alice.variants"], "Assets/Layer/alice.custom", false), _catalog);
        await operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare));
        Assert.That(ImageVariantsCodec.Decode(await Compiled("Layer/alice.custom")).Variants, Has.Count.EqualTo(2));
        File.WriteAllText(Path.Combine(_root, "Assets/Layer/alice.custom"), "{}");
        Assert.That(new ProjectValidationService(_root).Validate(catalog: _catalog).Diagnostics.Any(item => item.Code == "variants.invalid"), Is.True);
    }

    [Test]
    public async Task Export_uses_compiled_definition_and_keeps_custom_catalog_through_verification()
    {
        var types = new ResourceTypeCatalogBuilder()
            .Add<SpriteAssetMeta>("sprite", ".png")
            .Add<AssetMeta>("audio", ".ogg")
            .Add<AssetMeta>("video", ".mp4")
            .Add<DependentAssetMeta>("image-variants", ".variants")
            .Add<DependentAssetMeta>("graph", ".galgraph")
            .Add<DependentAssetMeta>("group", ".galgroup")
            .Add<CustomMeta>("custom-test", ".customtest").Build();
        PutAsset("extra.customtest", "custom-test", "custom");
        var before = await Compiled("alice.variants", types);
        var result = await GamePackageExporter.ExportAsync("variants", "Variants", _root, Path.Combine(_root, "Output"), types, BuiltinGalleryTypes.CreateCatalog(types), _catalog);
        Assert.That(result.Success, Is.True, result.Error);
        using var zip = ZipFile.OpenRead(result.PackagePath!);
        using var memory = new MemoryStream();
        using (var stream = zip.GetEntry("Assets/Paks/000-base.pak")!.Open()) await stream.CopyToAsync(memory);
        using var archive = Archive.Deserialize("assets", memory.ToArray(), types);
        Assert.That(archive.GetAssetByPath("alice.variants")!.ReadAllBytes(), Is.EqualTo(before));
        Assert.That(archive.GetAssetByPath("extra.customtest")!.Metadata, Is.TypeOf<CustomMeta>());
    }

    public sealed class CustomMeta : AssetMeta;
}
