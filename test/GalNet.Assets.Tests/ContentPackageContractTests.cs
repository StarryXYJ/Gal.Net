using System.Buffers.Binary;
using System.Text;
using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class ContentPackageContractTests
{
    private static readonly IResourceTypeCatalog Types = BuiltinResourceTypes.CreateCatalog();

    [Test]
    public void Pak_rejects_declared_length_mismatches_during_decompression([Values] CompressionMode mode, [Values(128, 1024)] int declaredLength)
    {
        var meta = new SpriteAssetMeta { Id = "bounded", Path = "bounded.png", TypeId = "sprite" };
        var pak = PakBuilder.Build("assets", [new GameFile(meta.Id, meta.Path, meta.TypeId, meta, new byte[512])], Types, mode);
        using (var stream = new MemoryStream(pak, writable: true))
        using (var reader = new BinaryReader(stream))
        {
            stream.Position = 8;
            stream.Position += sizeof(int) + BinaryPrimitives.ReadInt32LittleEndian(pak.AsSpan(8));
            Assert.That(reader.ReadInt32(), Is.EqualTo(1));
            for (var i = 0; i < 4; i++)
            {
                var length = reader.ReadInt32();
                stream.Position += length;
            }
            stream.Position += sizeof(long); // data offset precedes the original length
            BinaryPrimitives.WriteInt64LittleEndian(pak.AsSpan((int)stream.Position), declaredLength);
        }

        using var archive = Archive.Deserialize("assets", pak, Types);
        var error = Assert.Throws<InvalidDataException>(() => archive.GetAsset("bounded"));
        Assert.That(error!.Message, Does.Contain(declaredLength < 512 ? "exceeds" : "shorter"));
    }

    [Test]
    public void Pak_cancellation_is_preserved_through_interface_and_deserialization()
    {
        var pak = PakBuilder.Build("assets", [Sprite("a")], Types);
        using IArchive archive = Archive.Deserialize("assets", pak, Types);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => archive.GetAsset("a", cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => archive.GetAssetByPath("a.png", cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => Archive.Deserialize("assets", [], Types, cancellation.Token));
    }

    [TestCase("base")]
    [TestCase("story")]
    [TestCase("override")]
    public void Purpose_roundtrips_separately_from_resource_types(string kind)
    {
        var descriptor = new PakDescriptor
        {
            Kind = kind,
            ProjectId = "game",
            BuildId = "build",
            PackageId = kind == "base" ? "" : "extra",
            Priority = kind == "override" ? 3 : 0,
            Mappings = kind == "override" ? new() { ["original"] = "replacement" } : []
        };
        var bytes = PakBuilder.Build("ignored-filename", [Sprite("replacement")], Types, descriptor: descriptor);
        using var archive = Archive.Deserialize("renamed", bytes, Types);
        Assert.Multiple(() =>
        {
            Assert.That(archive.Descriptor.Kind, Is.EqualTo(kind));
            Assert.That(archive.Descriptor.ProjectId, Is.EqualTo("game"));
            Assert.That(archive.GetAsset("REPLACEMENT")!.TypeId, Is.EqualTo("sprite"));
            Assert.That(archive.Descriptor.Mappings, Is.EquivalentTo(descriptor.Mappings));
        });
        archive.Descriptor.Mappings.Clear();
        Assert.That(archive.Descriptor.Mappings.Count, Is.EqualTo(descriptor.Mappings.Count));
    }

    [Test]
    public void Purpose_rejects_unknown_kinds_invalid_combinations_and_missing_targets()
    {
        PakDescriptor[] invalid =
        [
            new() { Kind = "unknown" },
            new() { Kind = "story", PackageId = "extra" },
            new() { Priority = 1 },
            new() { PartIndex = 1 },
            new() { Kind = "override", PackageId = "extra", PartCount = 2 },
            new() { Kind = "override", PackageId = "extra", Mappings = new() { ["a"] = "b", ["b"] = "a" } },
            new() { Kind = "override", PackageId = "extra", Mappings = new(StringComparer.Ordinal) { ["a"] = "B", ["b"] = "A" } },
            new() { Kind = "override", PackageId = "extra", Mappings = new() { ["a"] = "missing" } }
        ];
        foreach (var descriptor in invalid)
            Assert.Throws<InvalidDataException>(() => PakBuilder.Build("assets", [Sprite("b")], Types, descriptor: descriptor));
    }

    [Test]
    public void Pak_order_is_deterministic_and_hashes_actual_bytes_not_stale_file_hash()
    {
        var a = Sprite("a");
        var b = Sprite("b");
        var stale = new GameFile(a.Id, a.Path, a.TypeId, a.Metadata, a.ReadAllBytes(), new string('0', 64));
        var first = PakBuilder.Build("first", [b, stale], Types);
        Assert.That(first, Is.EqualTo(PakBuilder.Build("second", [a, b], Types)));
        using var archive = Archive.Deserialize("assets", first, Types);
        Assert.That(archive.GetAsset("a")!.Hash, Is.EqualTo(CryptoHelper.HashSHA256(a.ReadAllBytes())));
    }

    [Test]
    public void Pak_rejects_old_version_truncation_duplicate_identity_and_corrupt_data()
    {
        var original = PakBuilder.Build("assets", [Sprite("a")], Types);
        var old = original.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(old.AsSpan(4), 2);
        Assert.Throws<InvalidDataException>(() => Archive.Deserialize("assets", old, Types));
        foreach (var size in new[] { 0, 4, 8, 12, original.Length - 1 })
            Assert.Throws<InvalidDataException>(() => Archive.Deserialize("assets", original[..size], Types));
        Assert.Throws<InvalidDataException>(() => PakBuilder.Build("assets", [Sprite("a"), Sprite("A")], Types));
        var corrupt = original.ToArray();
        corrupt[^1] ^= 1;
        using var archive = Archive.Deserialize("assets", corrupt, Types);
        Assert.Throws<InvalidDataException>(() => archive.GetAsset("a"));
    }

    [TestCase("{\"kind\":\"base\",\"kind\":\"story\"}")]
    [TestCase("{\"kind\":\"other\"}")]
    [TestCase("{\"kind\":\"base\",\"typo\":true}")]
    public void Pak_control_json_is_strict(string json)
    {
        var pak = PakBuilder.Build("assets", [], Types);
        var previousLength = BinaryPrimitives.ReadInt32LittleEndian(pak.AsSpan(8));
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(pak.AsSpan(0, 8));
            var control = Encoding.UTF8.GetBytes(json);
            writer.Write(control.Length);
            writer.Write(control);
            writer.Write(pak.AsSpan(12 + previousLength));
        }
        Assert.Throws<InvalidDataException>(() => Archive.Deserialize("assets", stream.ToArray(), Types));
    }

    [Test]
    public void Closure_distinguishes_leaves_unknowns_and_declared_dynamic_roots()
    {
        var unknown = new GameFile("dynamic", "dynamic.bin", "data", new AssetMeta
        { Id = "dynamic", Path = "dynamic.bin", TypeId = "data" }, []);
        var root = Dependent("root", [new("a", "sprite"), new("dynamic")]);
        var files = new[] { root, Sprite("a"), unknown, Sprite("unused") };
        var result = AssetDependencyCollector.Collect(files, [" ROOT "], Types);
        Assert.That(result.Files.Select(file => file.Id), Is.EquivalentTo(new[] { "root", "a", "dynamic" }));
        Assert.That(result.IncompleteAssetIds, Is.EqualTo(new[] { "dynamic" }));
        Assert.That(AssetDependencyCollector.Collect(files, ["root"], Types, declaredComplete: ["dynamic"]).Complete, Is.True);
    }

    [Test]
    public void Closure_checks_each_reference_type_handles_cycles_and_honors_cancellation()
    {
        var first = Dependent("a", [new("b")]);
        var second = Dependent("b", [new("a")]);
        Assert.That(AssetDependencyCollector.Collect([first, second], ["a"], Types).Files, Has.Count.EqualTo(2));
        Assert.Throws<InvalidDataException>(() => AssetDependencyCollector.Collect([first], ["a"], Types));
        var badType = Dependent("root", [new("sprite", "sprite"), new("sprite", "audio")]);
        Assert.Throws<InvalidDataException>(() => AssetDependencyCollector.Collect([badType, Sprite("sprite")], ["root"], Types));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => AssetDependencyCollector.Collect([first, second], ["a"], Types, cancellationToken: cancelled.Token));
    }

    [Test]
    public void Only_graph_references_may_cross_an_optional_story_boundary()
    {
        var types = new ResourceTypeCatalogBuilder().Add<DependentAssetMeta>("graph").Add<DependentAssetMeta>("group").Build();
        var meta = new DependentAssetMeta
        {
            Id = "graph",
            TypeId = "graph",
            Path = "graph",
            DependenciesComplete = true,
            Dependencies = [new("optional", "group", "extra")]
        };
        var graph = new GameFile(meta.Id, meta.Path, meta.TypeId, meta, []);
        Assert.That(AssetDependencyCollector.Collect([graph], ["graph"], types).Files, Has.Count.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => AssetDependencyCollector.Collect([graph], ["graph"], types, storyPackage: "extra"));
        var group = Dependent("group", [new("optional", null, "extra")]);
        Assert.Throws<InvalidDataException>(() => AssetDependencyCollector.Collect([group], ["group"], Types));
    }

    [Test]
    public async Task Compiled_atlas_dependencies_match_payload_and_never_rewrite_sidecar()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "galnet-deps-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "images"));
            await File.WriteAllTextAsync(Path.Combine(root, "images", "idle.png"), "image");
            await File.WriteAllTextAsync(Path.Combine(root, "images", "idle.png.meta"), """{"id":"sprite","type":"sprite"}""");
            await File.WriteAllTextAsync(Path.Combine(root, "atlas.variants"), """{"folder":"images","default":"idle"}""");
            const string sourceMeta = """{"id":"atlas","type":"image-variants","future":true}""";
            await File.WriteAllTextAsync(Path.Combine(root, "atlas.variants.meta"), sourceMeta);
            using var provider = new LocalFileProvider(root, Types);
            using var source = await provider.OpenArchiveAsync("assets");
            var atlas = source.GetAsset("atlas")!;
            var metadata = (DependentAssetMeta)atlas.Metadata;
            Assert.That(metadata.Dependencies, Is.EqualTo(new[] { new AssetDependency("sprite", "sprite") }));
            Assert.That(metadata.ExtensionData!["future"].GetBoolean(), Is.True);
            Assert.That(await File.ReadAllTextAsync(Path.Combine(root, "atlas.variants.meta")), Is.EqualTo(sourceMeta));
            var selected = AssetDependencyCollector.Collect(source.AssetIds.Select(id => source.GetAsset(id)!), ["atlas"], Types);
            using var restored = Archive.Deserialize("assets", PakBuilder.Build("assets", selected.Files, Types), Types);
            Assert.That(((IAssetDependencies)restored.GetAsset("atlas")!.Metadata).GetDependencies().Direct, Is.EqualTo(metadata.Dependencies));
            var bytes = PakBuilder.Build("assets", selected.Files, Types);
            var json = Encoding.UTF8.GetString(bytes);
            var start = json.IndexOf("\"expectedTypeId\":\"sprite\"", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThan(0));
            // Only ASCII precedes this field in this fixture, so character and byte offsets agree.
            "audio "u8.CopyTo(bytes.AsSpan(start + "\"expectedTypeId\":\"".Length));
            Assert.Throws<InvalidDataException>(() => Archive.Deserialize("assets", bytes, Types));
            metadata.Dependencies.Clear();
            Assert.Throws<InvalidDataException>(() => PakBuilder.Build("assets", [atlas], Types));
        }
        finally { Directory.Delete(root, true); }
    }

    private static GameFile Sprite(string id) => new(id, id + ".png", "sprite",
        new SpriteAssetMeta { Id = id, Path = id + ".png", TypeId = "sprite" }, "image"u8.ToArray());

    private static GameFile Dependent(string id, List<AssetDependency> dependencies) => new(id, id + ".bin", "data",
        new DependentAssetMeta { Id = id, Path = id + ".bin", TypeId = "data", Dependencies = dependencies, DependenciesComplete = true }, []);
}
