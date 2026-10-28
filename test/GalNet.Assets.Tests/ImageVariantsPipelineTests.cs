using System.Text.Json;
using GalNet.Assets.Provider;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Variants;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class ImageVariantsPipelineTests
{
    private string _root = null!;
    [SetUp] public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "galnet-variants-" + Guid.NewGuid().ToString("N"))).FullName;
    [TearDown] public void TearDown() => Directory.Delete(_root, true);

    private string Assets => Path.Combine(_root, "Assets");
    private static readonly IResourceTypeCatalog Types = BuiltinResourceTypes.CreateCatalog();

    private void Asset(string path, string id, string? type = null, string? content = null)
    {
        var full = Path.Combine(Assets, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content ?? "image-bytes");
        File.WriteAllText(full + ".meta", JsonSerializer.Serialize(new { id, type, path = "stale/path" }));
    }

    private void Fixture(string folder = "./alice")
    {
        foreach (var key in new[] { "idle_coat", "idle_skirt", "smile_coat", "smile_skirt", "weap" })
            Asset("alice/" + key + ".png", "sprite-" + key, "sprite");
        Asset("alice.variants", "atlas", "image-variants", JsonSerializer.Serialize(new ImageVariantsSource
        {
            Folder = folder,
            Default = "idle_coat",
            Dimensions = [new() { Key = "expression", Label = "表情" }, new() { Key = "outfit", Label = "服装" }]
        }));
    }

    [Test]
    public async Task Local_and_real_Pak_provider_decode_identically_without_the_author_directory()
    {
        Fixture();
        byte[] localBytes;
        using (var provider = new LocalFileProvider(Assets, Types))
        using (var archive = await provider.OpenArchiveAsync("assets"))
        {
            var file = archive.GetAsset("atlas")!;
            Assert.That(file.Metadata, Is.TypeOf<DependentAssetMeta>());
            Assert.That(file.Path, Is.EqualTo("alice.variants"));
            localBytes = file.ReadAllBytes();
            Assert.That(System.Text.Encoding.UTF8.GetString(localBytes), Does.Not.Contain("folder"));
            var pak = PakBuilder.Build("assets", archive.AssetIds.Select(id => archive.GetAsset(id)!).ToArray(), resourceTypes: Types);
            File.WriteAllBytes(Path.Combine(_root, "assets.pak"), pak);
        }
        using (var manager = new AssetManager([new LocalFileProvider(Assets, Types)]))
        {
            manager.RegisterImageVariantsDecoder();
            using var handle = await manager.AcquireAsync<ImageVariantsDefinition>("atlas");
            Assert.That(handle!.Value.Variants, Has.Count.EqualTo(5));
        }
        Directory.Move(Assets, Assets + "-removed");
        using var installed = new AssetManager([new PakFileProvider(_root, Types)]);
        installed.RegisterImageVariantsDecoder();
        using var definition = await installed.AcquireAsync<ImageVariantsDefinition>("atlas");
        Assert.That(ImageVariantsCodec.Encode(definition!.Value), Is.EqualTo(localBytes));
        var request = ImageVariantsResolver.Initialize(definition.Value, new Dictionary<string, string> { ["outfit"] = "skirt" });
        var special = ImageVariantsResolver.Select(definition.Value, request.Request, "expression", "weap");
        Assert.That(special.Variant.AssetId, Is.EqualTo("sprite-weap"));
        using var sprite = await installed.AcquireAsync<byte[]>(special.Variant.AssetId);
        Assert.That(sprite!.Value, Is.EqualTo("image-bytes"u8.ToArray()));
    }

    [TestCase("..\\outside")]
    [TestCase("../../outside")]
    [TestCase("/outside")]
    [TestCase("C:\\outside")]
    [TestCase("./missing")]
    public void Folder_must_exist_within_Assets(string folder)
    {
        Fixture(folder);
        using var provider = new LocalFileProvider(Assets, Types);
        var error = Assert.ThrowsAsync<InvalidDataException>(() => provider.OpenArchiveAsync("assets"));
        Assert.That(error!.Message, Does.Contain("alice.variants"));
    }

    [Test]
    public async Task Backslashes_and_parent_segments_resolve_relative_to_the_descriptor()
    {
        Fixture();
        Asset("descriptions/alice.variants", "atlas-nested", "image-variants",
            """{"folder":"..\\alice","default":"weap"}""");
        using var provider = new LocalFileProvider(Assets, Types);
        using var archive = await provider.OpenArchiveAsync("assets");
        Assert.That(ImageVariantsCodec.Decode(archive.GetAsset("atlas-nested")!.ReadAllBytes()).Default.AssetId, Is.EqualTo("sprite-weap"));
    }

    [TestCase("image-sidecar")]
    [TestCase("descriptor-sidecar")]
    [TestCase("source-image")]
    [TestCase("wrong-type")]
    [TestCase("duplicate")]
    [TestCase("case-collision")]
    [TestCase("default")]
    public void Invalid_sources_are_not_repaired_or_silently_omitted(string problem)
    {
        Fixture();
        switch (problem)
        {
            case "image-sidecar": File.Delete(Path.Combine(Assets, "alice/weap.png.meta")); break;
            case "descriptor-sidecar": File.Delete(Path.Combine(Assets, "alice.variants.meta")); break;
            case "source-image": File.Delete(Path.Combine(Assets, "alice/weap.png")); break;
            case "wrong-type": Asset("alice/weap.png", "sprite-weap", "audio"); break;
            case "duplicate": Asset("alice/weap.webp", "second", "sprite"); break;
            case "case-collision": Asset("alice/Weap.webp", "second", "sprite"); break;
            case "default": File.WriteAllText(Path.Combine(Assets, "alice.variants"), """{"folder":"alice","default":"missing"}"""); break;
        }
        var before = Directory.GetFiles(Assets, "*", SearchOption.AllDirectories).Order().ToArray();
        using var provider = new LocalFileProvider(Assets, Types);
        Assert.ThrowsAsync<InvalidDataException>(() => provider.OpenArchiveAsync("assets"));
        Assert.That(Directory.GetFiles(Assets, "*", SearchOption.AllDirectories).Order().ToArray(), Is.EqualTo(before));
    }

    [Test]
    public async Task Invalidating_recompiles_new_files_and_edits_without_reusing_old_definition()
    {
        Fixture();
        using var provider = new LocalFileProvider(Assets, Types);
        using var oldArchive = await provider.OpenArchiveAsync("assets");
        var oldBytes = oldArchive.GetAsset("atlas")!.ReadAllBytes();
        Asset("alice/angry_skirt.png", "angry", "sprite");
        provider.Invalidate();
        using var newArchive = await provider.OpenArchiveAsync("assets");
        var updated = ImageVariantsCodec.Decode(newArchive.GetAsset("atlas")!.ReadAllBytes());
        Assert.That(updated.Variants, Has.Count.EqualTo(6));
        Assert.That(ImageVariantsCodec.Decode(oldBytes).Variants, Has.Count.EqualTo(5));
        File.WriteAllText(Path.Combine(Assets, "alice.variants"), "{}");
        provider.Invalidate();
        Assert.ThrowsAsync<InvalidDataException>(() => provider.OpenArchiveAsync("assets"));
    }

    [Test]
    public async Task Cancellation_and_disposal_cannot_return_a_stale_archive()
    {
        Fixture();
        var provider = new LocalFileProvider(Assets, Types);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => provider.OpenArchiveAsync("assets", cancellation.Token));
        using var valid = await provider.OpenArchiveAsync("assets");
        provider.Dispose();
        Assert.ThrowsAsync<ObjectDisposedException>(() => provider.OpenArchiveAsync("assets"));
    }

    [TestCase("{\"folder\":\"alice\",\"folder\":\"outside\",\"default\":\"weap\"}")]
    [TestCase("{\"folder\":\"alice\",\"default\":\"weap\",\"typo\":true}")]
    public void Source_parser_rejects_duplicate_or_unknown_properties(string json)
    {
        Assert.That(() => ImageVariantsCodec.ReadSource(System.Text.Encoding.UTF8.GetBytes(json)), Throws.Exception);
    }

    [Test]
    public void Runtime_decoder_rejects_author_payload_and_malformed_compiled_facts()
    {
        Assert.That(() => ImageVariantsCodec.Decode("""{"kind":"image-variants","version":1,"default":"idle_coat","dimensions":[{"key":"d1"}],"variants":[{"key":"idle_coat","assetId":"sprite"}]}"""u8.ToArray()), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => ImageVariantsCodec.Decode("""{"folder":"alice","default":"weap"}"""u8.ToArray()), Throws.Exception);
        Assert.That(() => ImageVariantsCodec.Decode("""{"kind":"image-variants","version":99,"default":"weap","dimensions":[],"variants":[]}"""u8.ToArray()), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => ImageVariantsCodec.Decode("""{"kind":"image-variants","version":1,"default":"weap","dimensions":[{"key":"d1"}],"variants":[{"key":"weap","assetId":""}]}"""u8.ToArray()), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Directory_links_are_rejected_without_following_them()
    {
        Fixture();
        var link = Path.Combine(Assets, "linked");
        DirectoryLinks.Create(link, Path.Combine(Assets, "alice"));
        try
        {
            using var provider = new LocalFileProvider(Assets, Types);
            Assert.ThrowsAsync<InvalidDataException>(() => provider.OpenArchiveAsync("assets"));
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public async Task Prepared_catalog_outlives_resource_leases_and_is_isolated_from_later_scopes()
    {
        Fixture();
        ImageVariantsCatalog original;
        using (var first = new AssetManager([new LocalFileProvider(Assets, Types)]))
        {
            first.RegisterImageVariantsDecoder();
            original = await first.LoadImageVariantsCatalogAsync();
        }
        Asset("alice/weap.png", "new-weap", "sprite");
        using var second = new AssetManager([new LocalFileProvider(Assets, Types)]);
        second.RegisterImageVariantsDecoder();
        var updated = await second.LoadImageVariantsCatalogAsync();
        Assert.That(original.Get("ATLAS").Variants.Single(item => item.Key == "weap").AssetId, Is.EqualTo("sprite-weap"));
        Assert.That(updated.Get("atlas").Variants.Single(item => item.Key == "weap").AssetId, Is.EqualTo("new-weap"));
        Assert.That(() => original.Get("missing"), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Catalog_preparation_rejects_missing_or_non_sprite_dependencies_in_a_Pak(bool wrongType)
    {
        Fixture();
        byte[] payload;
        using (var provider = new LocalFileProvider(Assets, Types))
        using (var archive = await provider.OpenArchiveAsync("assets")) payload = archive.GetAsset("atlas")!.ReadAllBytes();
        var meta = new DependentAssetMeta { Id = "atlas", Path = "alice.variants", TypeId = "image-variants",
            DependenciesComplete = true, Dependencies = ImageVariantsCodec.Decode(payload).Variants
                .Select(variant => new AssetDependency(variant.AssetId, "sprite")).ToList() };
        var files = new List<IGameFile> { new GameFile(meta.Id, meta.Path, meta.TypeId, meta, payload) };
        foreach (var key in new[] { "idle_coat", "idle_skirt", "smile_coat", "smile_skirt", "weap" })
        {
            if (key == "weap" && !wrongType) continue;
            AssetMeta sprite = key == "weap" ? new AssetMeta() : new SpriteAssetMeta();
            sprite.Id = "sprite-" + key;
            sprite.TypeId = key == "weap" ? "audio" : "sprite";
            sprite.Path = "alice/" + key + ".png";
            files.Add(new GameFile(sprite.Id, sprite.Path, sprite.TypeId, sprite, [1]));
        }
        File.WriteAllBytes(Path.Combine(_root, "assets.pak"), PakBuilder.Build("assets", files, Types));
        using var manager = new AssetManager([new PakFileProvider(_root, Types)]);
        manager.RegisterImageVariantsDecoder();
        Assert.ThrowsAsync<InvalidDataException>(() => manager.LoadImageVariantsCatalogAsync());
    }

    [Test]
    public void Cancelled_preparation_does_not_publish_an_empty_successful_catalog()
    {
        using var manager = new AssetManager();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => manager.LoadImageVariantsCatalogAsync(cancellation.Token));
    }
}
