using System.Text;
using GalNet.Assets.Provider;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Variants;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;

namespace GalNet.Assets.Tests;

public sealed class ResourceOverrideTests
{
    private static readonly IResourceTypeCatalog Types = BuiltinResourceTypes.CreateCatalog();
    private static IGameFile File(string id, string content, string type = "sprite")
    {
        var meta = type == "sprite" ? (AssetMeta)new SpriteAssetMeta() : new AssetMeta();
        meta.Id = id; meta.Path = id + (type == "sprite" ? ".png" : ".bin"); meta.TypeId = type;
        return new GameFile(id, meta.Path, type, meta, Encoding.UTF8.GetBytes(content));
    }
    private static ResourceOverrideDefinition Definition(string id, int priority = 0, params (string From, string To)[] mappings) =>
        new() { Id = id, Priority = priority, Mappings = mappings.ToDictionary(pair => pair.From, pair => pair.To) };
    private static ResourceOverrideSource Patch(ResourceOverrideDefinition definition, params IGameFile[] available)
    {
        var bytes = ResourceOverrideBuilder.Build("project", definition, available, Types);
        var provider = new MemoryProvider(bytes);
        using var archive = provider.OpenArchive("assets");
        return new(((IPakArchive)archive).Descriptor, CryptoHelper.HashSHA256(bytes), archive.AssetIds.Select(id => new ResourceFileSource(archive.GetAsset(id)!, provider)).ToArray());
    }
    private static ResourceFileSource[] Base(params IGameFile[] files)
    {
        var provider = new MemoryProvider(PakBuilder.Build("assets", files, Types));
        using var archive = provider.OpenArchive("assets");
        return archive.AssetIds.Select(id => new ResourceFileSource(archive.GetAsset(id)!, provider)).ToArray();
    }

    [Test]
    public async Task Failed_replacement_read_reports_logical_identity_and_backing_source()
    {
        var original = File("original", "A", "audio");
        var replacement = File("replacement", "B", "audio");
        var patch = Patch(Definition("voice", 0, ("original", "replacement")), original, replacement);
        var error = new InvalidDataException("changed patch hash");
        patch = patch with { Files = patch.Files.Select(file => file with { Provider = new BrokenPatchProvider(error) }).ToArray() };
        using var manager = new AssetManager([new ResourceOverlayProvider(Base(original), [patch], Types)]);
        var reports = new List<AssetDiagnostic>();
        manager.Diagnostic += reports.Add;
        Assert.That(await manager.AcquireAsync<byte[]>("original"), Is.Null);
        var report = reports.Single();
        Assert.That(report.Stage, Is.EqualTo(AssetLoadStage.Read));
        Assert.That(report.Kind, Is.EqualTo(AssetFailureKind.InvalidData));
        Assert.That(report.AssetId, Is.EqualTo("original"));
        Assert.That(report.Path, Is.EqualTo(original.Path));
        Assert.That(report.SourceAssetId, Is.EqualTo("replacement"));
        Assert.That(report.Provider, Is.EqualTo("voice-patch.pak"));
        Assert.That(report.Exception, Is.SameAs(error));
    }

    private sealed class BrokenPatchProvider(Exception error) : IAssetProvider
    {
        public string Name => "voice-patch.pak";
        public bool Exists(string name) => true;
        public IArchive OpenArchive(string name) => throw error;
        public Task<IArchive> OpenArchiveAsync(string name, CancellationToken ct = default) => throw error;
    }

    [TestCase("sprite")]
    [TestCase("audio")]
    [TestCase("video")]
    public async Task LogicalFileAcquirePathAndMediaShareTheSameReplacement(string type)
    {
        var a = File("a", "coat-A", type); var b = File("b", "coat-B", type);
        var patch = Patch(Definition("clothes", 0, ("a", "b")), a, b);
        using var manager = new AssetManager([new ResourceOverlayProvider(Base(a), [patch], Types)]);
        var file = await manager.GetFileAsync(" a ");
        Assert.That(file!.Id, Is.EqualTo("a"));
        Assert.That(file.Path, Is.EqualTo(a.Path));
        Assert.That(file.Metadata.Id, Is.EqualTo("a"));
        Assert.That(Encoding.UTF8.GetString(file.ReadAllBytes()), Is.EqualTo("coat-B"));
        using var handle = await manager.AcquireAsync<string>("A");
        using var pathHandle = await manager.AcquireByPathAsync<string>(a.Path);
        Assert.That(handle!.Value, Is.EqualTo("coat-B"));
        Assert.That(pathHandle!.Value, Is.EqualTo("coat-B"));
        await using var materializer = new AssetFileMaterializer(manager);
        using var lease = await materializer.AcquireAsync("a");
        Assert.That(System.IO.File.ReadAllText(lease.Path), Is.EqualTo("coat-B"));
    }

    [Test]
    public async Task PriorityThenWholePakHashWinsRegardlessOfOrderOrDuplicates()
    {
        var a = File("a", "A"); var b = File("b", "B"); var c = File("c", "C");
        var left = Patch(Definition("left", 0, ("a", "b")), a, b);
        var right = Patch(Definition("right", 0, ("a", "c")), a, c);
        var expected = StringComparer.Ordinal.Compare(left.Sha256, right.Sha256) > 0 ? "B" : "C";
        foreach (var order in new[] { new[] { left, right, left }, new[] { right, left } })
        {
            using var manager = new AssetManager([new ResourceOverlayProvider(Base(a), order, Types)]);
            using var handle = await manager.AcquireAsync<string>("a");
            Assert.That(handle!.Value, Is.EqualTo(expected));
        }
        var high = Patch(Definition("higher", 3, ("a", "b")), a, b);
        using var higher = new AssetManager([new ResourceOverlayProvider(Base(a), [right, high], Types)]);
        using var selected = await higher.AcquireAsync<string>("a");
        Assert.That(selected!.Value, Is.EqualTo("B"));
    }

    [Test]
    public async Task MappingOnlyAppliesOnceAndCarriedTargetsCannotImplicitlyOverrideBase()
    {
        var a = File("a", "A"); var b = File("b", "B"); var c = File("c", "C");
        var patch = Patch(Definition("chain", 0, ("a", "b"), ("b", "c")), a, b, c);
        using var manager = new AssetManager([new ResourceOverlayProvider(Base(a, b), [patch], Types)]);
        using var aValue = await manager.AcquireAsync<string>("a");
        using var bValue = await manager.AcquireAsync<string>("b");
        Assert.That(aValue!.Value, Is.EqualTo("B"));
        Assert.That(bValue!.Value, Is.EqualTo("C"));
        var changedB = File("b", "changed-B");
        var single = Patch(Definition("one", 0, ("a", "b")), a, changedB);
        using var originalB = new AssetManager([new ResourceOverlayProvider(Base(a, b), [single], Types)]);
        using var unchanged = await originalB.AcquireAsync<string>("b");
        Assert.That(unchanged!.Value, Is.EqualTo("B"));
    }

    [Test]
    public async Task AtlasCarriesReplacementSpritesAndPreservesTheOriginalLogicalAtlas()
    {
        var aSprite = File("a-sprite", "A"); var bSprite = File("b-sprite", "B");
        var a = Atlas("a", "a-sprite"); var b = Atlas("b", "b-sprite");
        var patch = Patch(Definition("clothes", 0, ("a", "b")), a, aSprite, b, bSprite);
        Assert.That(patch.Files.Select(file => file.Index.Id), Is.EquivalentTo(new[] { "b", "b-sprite" }));
        using var manager = new AssetManager([new ResourceOverlayProvider(Base(a, aSprite), [patch], Types)]);
        manager.RegisterImageVariantsDecoder();
        using var atlas = await manager.AcquireAsync<ImageVariantsDefinition>("a");
        Assert.That(atlas!.Value.Default.AssetId, Is.EqualTo("b-sprite"));
        using var pixels = await manager.AcquireAsync<string>(atlas.Value.Default.AssetId);
        Assert.That(pixels!.Value, Is.EqualTo("B"));
    }

    [Test]
    public void InvalidTargetsTypesStoryResourcesAndChangedDependencyIdsAreRejected()
    {
        var a = File("a", "A");
        Assert.Throws<InvalidDataException>(() => Patch(Definition("missing", 0, ("a", "missing")), a));
        Assert.Throws<InvalidDataException>(() => Patch(Definition("type", 0, ("a", "b")), a, File("b", "B", "audio")));
        Assert.Throws<InvalidDataException>(() => Patch(Definition("graph", 0, ("a", "b")), File("a", "A", "graph"), File("b", "B", "graph")));
        var original = Atlas("a", "sprite"); var replacement = Atlas("b", "sprite");
        var patch = Patch(Definition("conflict", 0, ("a", "b")), original, replacement, File("sprite", "changed"));
        Assert.Throws<InvalidDataException>(() => new ResourceOverlayProvider(Base(original, File("sprite", "base")), [patch], Types));
        Assert.Throws<InvalidDataException>(() => Patch(Definition("shape", 0, ("a", "b")), original, Atlas("b", "sprite", "different"), File("sprite", "S")));
    }

    [Test]
    public void OverrideDefinitionKeepsCaseInsensitiveResourceIdentityAfterJsonRoundtrip()
    {
        var definition = Definition("case", 0, ("ORIGINAL", "replacement"));
        var saved = GalNet.Core.Serialization.JsonCodec.Serialize(definition);
        var restored = GalNet.Core.Serialization.JsonCodec.Deserialize<ResourceOverrideDefinition>(saved)!;
        Assert.That(restored.Mappings["original"], Is.EqualTo("replacement"));
        restored.Mappings["Original"] = "new-replacement";
        Assert.That(restored.Mappings.Count, Is.EqualTo(1));
    }

    [Test]
    public void EquivalentMappingOrdersHaveIdenticalPakHashes()
    {
        var files = new[] { File("a", "A"), File("b", "B"), File("c", "C"), File("d", "D") };
        var first = ResourceOverrideBuilder.Build("project", Definition("same", 0, ("a", "b"), ("c", "d")), files, Types);
        var second = ResourceOverrideBuilder.Build("project", Definition("same", 0, ("c", "d"), ("a", "b")), files.Reverse().ToArray(), Types);
        Assert.That(CryptoHelper.HashSHA256(first), Is.EqualTo(CryptoHelper.HashSHA256(second)));
    }

    private static IGameFile Atlas(string id, string sprite, string key = "idle")
    {
        var definition = ImageVariantsBuilder.Build(new() { Folder = ".", Default = key }, [new ImageVariantFile(key + ".png", sprite)]).Definition!;
        var metadata = new DependentAssetMeta
        {
            Id = id,
            TypeId = "image-variants",
            Path = id + ".variants",
            DependenciesComplete = true,
            Dependencies = [new(sprite, "sprite")]
        };
        return new GameFile(id, metadata.Path, metadata.TypeId, metadata, ImageVariantsCodec.Encode(definition));
    }
    private sealed class MemoryProvider(byte[] bytes) : IAssetProvider
    {
        public string Name => "memory";
        public bool Exists(string name) => true;
        public IArchive OpenArchive(string name) => Archive.Deserialize(name, bytes, Types);
        public Task<IArchive> OpenArchiveAsync(string name, CancellationToken ct = default) => Task.FromResult(OpenArchive(name));
    }
}
