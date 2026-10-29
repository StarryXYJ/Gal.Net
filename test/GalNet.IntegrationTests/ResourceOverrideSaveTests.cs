using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Contracts.Builtins.Variants;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public sealed class ResourceOverrideSaveTests
{
    private static readonly IResourceTypeCatalog Types = BuiltinResourceTypes.CreateCatalog();
    [Test]
    public async Task InstallingAndRemovingAnAtlasOverrideRebindsSavedSelectionToTheCurrentClothes()
    {
        var original = Atlas("atlas-a", "sprite-a"); var replacement = Atlas("atlas-b", "sprite-b");
        var spriteA = Sprite("sprite-a"); var spriteB = Sprite("sprite-b");
        var baseline = Sources(PakBuilder.Build("assets", [original, spriteA], Types));
        var bytes = ResourceOverrideBuilder.Build("project", new ResourceOverrideDefinition { Id = "clothes", Mappings = new() { ["atlas-a"] = "atlas-b" } },
            [original, spriteA, replacement, spriteB], Types);
        using var patch = Archive.Deserialize("assets", bytes, Types);
        var overrideSource = new ResourceOverrideSource(patch.Descriptor, CryptoHelper.HashSHA256(bytes), Sources(bytes));
        using var baseAssets = new AssetManager([new ResourceOverlayProvider(baseline, [], Types)]);
        using var patchedAssets = new AssetManager([new ResourceOverlayProvider(baseline, [overrideSource], Types)]);
        baseAssets.RegisterImageVariantsDecoder(); patchedAssets.RegisterImageVariantsDecoder();
        var baseCatalog = await baseAssets.LoadImageVariantsCatalogAsync();
        var patchedCatalog = await patchedAssets.LoadImageVariantsCatalogAsync();
        using var sourceView = View(new(), baseCatalog);
        var source = new GameRuntime(null, "root", stateCatalog: sourceView.StateCatalog);
        sourceView.Dispatch(new PrimitiveEntry("variants.show", JsonSerializer.SerializeToElement(new { handleId = "hero", assetId = "atlas-a" })), source, default);
        var saved = source.CreateSnapshot();
        var originalJson = saved.States["builtin.layers"].Payload.GetRawText();
        var presenter = new Presenter(); using var patchedView = View(presenter, patchedCatalog);
        var patchedRuntime = new GameRuntime(null, "root", stateCatalog: patchedView.StateCatalog);
        await using var patchedEngine = new GameEngine(Graph(), patchedRuntime, patchedView);
        await patchedEngine.RestoreFromAsync(saved);
        await using var patchedPresentation = new StateRestoreSession(patchedView);
        await patchedPresentation.RestoreAsync(patchedRuntime);
        Assert.That(presenter.Last, Is.EqualTo("sprite-b"));
        Assert.That(patchedRuntime.State.Table<Layer>().Get("hero").Variants!.UsesResourceOverride, Is.True);
        Assert.That(saved.States["builtin.layers"].Payload.GetRawText(), Is.EqualTo(originalJson), "Rebinding must not mutate the input save.");
        Assert.That(patchedRuntime.State.Table<Layer>().Get("hero").Variants!.AssetId, Is.EqualTo("atlas-a"));
        var patchedSave = patchedRuntime.CreateSnapshot();
        var unpatchedPresenter = new Presenter(); using var unpatchedView = View(unpatchedPresenter, baseCatalog);
        var unpatched = new GameRuntime(null, "root", stateCatalog: unpatchedView.StateCatalog);
        await using var unpatchedEngine = new GameEngine(Graph(), unpatched, unpatchedView);
        await unpatchedEngine.RestoreFromAsync(patchedSave);
        await using var unpatchedPresentation = new StateRestoreSession(unpatchedView);
        await unpatchedPresentation.RestoreAsync(unpatched);
        Assert.That(unpatchedPresenter.Last, Is.EqualTo("sprite-a"));
        Assert.That(unpatched.State.Table<Layer>().Get("hero").Variants!.UsesResourceOverride, Is.False);
    }
    private static Graph Graph() => new() { RootNodeId = "root", Nodes = [new Group { Id = "root" }] };
    private static CompositeGameView View(Presenter presenter, ImageVariantsCatalog catalog) =>
        new GameRegistration().RegisterSchema(BuiltinSchemas.CreateLayersModule()).AddBuiltinLayers(presenter, catalog).BuildView();
    private static IGameFile Sprite(string id)
    {
        var meta = new SpriteAssetMeta { Id = id, TypeId = "sprite", Path = id + ".png" };
        return new GameFile(id, meta.Path, meta.TypeId, meta, [1]);
    }
    private static IGameFile Atlas(string id, string sprite)
    {
        var definition = ImageVariantsBuilder.Build(new() { Folder = ".", Default = "idle" }, [new("idle.png", sprite)]).Definition!;
        var meta = new DependentAssetMeta { Id = id, TypeId = "image-variants", Path = id + ".variants", DependenciesComplete = true, Dependencies = [new(sprite, "sprite")] };
        return new GameFile(id, meta.Path, meta.TypeId, meta, ImageVariantsCodec.Encode(definition));
    }
    private static ResourceFileSource[] Sources(byte[] bytes)
    {
        var provider = new MemoryProvider(bytes);
        using var archive = provider.OpenArchive("assets");
        return archive.AssetIds.Select(id => new ResourceFileSource(archive.GetAsset(id)!, provider)).ToArray();
    }
    private sealed class MemoryProvider(byte[] bytes) : IAssetProvider
    {
        public string Name => "memory";
        public bool Exists(string name) => true;
        public IArchive OpenArchive(string name) => Archive.Deserialize(name, bytes, Types);
        public Task<IArchive> OpenArchiveAsync(string name, CancellationToken ct = default) => Task.FromResult(OpenArchive(name));
    }
    private sealed class Presenter : ILayerPresenter
    {
        public string Last { get; private set; } = "";
        public void ShowLayer(LayerRenderRequest request) => Last = request.AssetId;
        public void ReplaceLayer(string handleId, string assetId) => Last = assetId;
        public void HideLayer(string handleId) { }
        public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds) { }
    }
}
