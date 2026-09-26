using GalNet.Core.Gallery;
using GalNet.Core.Variable;
using GalNet.Runtime.Runtime;
using GalNet.Storage.FileSystem;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GeneralTest.Storage;

public sealed class FileVariableServiceTests
{
    private string _profileDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _profileDirectory = Path.Combine(Path.GetTempPath(), "GalNet.Storage.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_profileDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_profileDirectory))
            Directory.Delete(_profileDirectory, true);
    }

    [Test]
    public async Task FlushPlayerVariablesAsync_PersistsOnlyPlayerScope()
    {
        var service = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_profileDirectory));
        var player = new GalVariable { Name = "score" };
        player.SetValue(42);
        var save = new GalVariable { Name = "health" };
        save.SetValue(7);

        service.NotifyVariableChanged(VariableScope.Player, "score", player);
        service.NotifyVariableChanged(VariableScope.Save, "health", save);
        await service.FlushPlayerVariablesAsync();

        var reloaded = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_profileDirectory));

        Assert.That(reloaded.GetSnapshot(VariableScope.Player)["score"].AsInt(), Is.EqualTo(42));
        Assert.That(reloaded.GetSnapshot(VariableScope.Save), Is.Empty);
    }

    [Test]
    public async Task GallerySystemVariablesAreInitializedAndPersistAcrossRuntimeReload()
    {
        var catalog = CreateGallery();
        var definitions = GalleryUnlockVariable.CreateDefinitions(catalog);
        var service = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_profileDirectory));
        service.ConfigureSystemVariables(definitions);

        Assert.Multiple(() =>
        {
            Assert.That(service.ResolveScope("gallery_opening_unlocked"), Is.EqualTo(VariableScope.Player));
            Assert.That(service.GetSnapshot(VariableScope.Player)["gallery_opening_unlocked"].AsBool(), Is.False);
        });

        var runtime = new GameRuntime(null, variableService: service);
        var snapshotBeforeUnlock = runtime.CreateSnapshot();
        runtime.SetVariable(GalleryUnlockVariable.GetRuntimeName("opening"), true);
        runtime.RestoreFrom(snapshotBeforeUnlock);

        Assert.That(service.GetSnapshot(VariableScope.Player)["gallery_opening_unlocked"].AsBool(), Is.True,
            "restoring a save-slot snapshot must not roll back Player-scoped Gallery state");

        await service.FlushPlayerVariablesAsync();
        var reloaded = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_profileDirectory));
        reloaded.ConfigureSystemVariables(definitions);

        Assert.That(reloaded.GetSnapshot(VariableScope.Player)["gallery_opening_unlocked"].AsBool(), Is.True);
    }

    [Test]
    public async Task GalleryDataSourceProjectsCatalogAndCurrentUnlockState()
    {
        var catalog = CreateGallery();
        var service = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_profileDirectory));
        service.ConfigureSystemVariables(GalleryUnlockVariable.CreateDefinitions(catalog));
        var source = new GalleryDataSource(catalog, service);

        Assert.That(source.GetTypes().Single().Items.Single().IsUnlocked, Is.False);

        var unlocked = new GalVariable { Name = GalleryUnlockVariable.GetName("opening") };
        unlocked.SetValue(true);
        service.NotifyVariableChanged(VariableScope.Player, unlocked.Name, unlocked);

        var data = source.GetTypes().Single();
        Assert.Multiple(() =>
        {
            Assert.That(data.Type.TypeId, Is.EqualTo("cg"));
            Assert.That(data.Type.ResourceTypeName, Is.EqualTo("sprite"));
            Assert.That(data.Items.Single().Item.ResourceId, Is.EqualTo("asset-opening"));
            Assert.That(data.Items.Single().IsUnlocked, Is.True);
        });
    }

    [Test]
    public async Task GalleryDataSourceOmitsRegisteredTypesWithoutItems()
    {
        var catalog = GalleryCatalog.Create(new GalleryConfiguration
        {
            Types =
            [
                new GalleryTypeRegistration { TypeId = "cg", ResourceTypeName = "sprite" },
                new GalleryTypeRegistration { TypeId = "audio", ResourceTypeName = "audio" }
            ],
            Items = [new GalleryItem { Id = "opening", TypeId = "cg", ResourceId = "asset-opening" }]
        });
        var service = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_profileDirectory));
        service.ConfigureSystemVariables(GalleryUnlockVariable.CreateDefinitions(catalog));

        var types = new GalleryDataSource(catalog, service).GetTypes();

        Assert.That(types.Select(type => type.Type.TypeId), Is.EqualTo(["cg"]));
    }

    private static GalleryCatalog CreateGallery() => GalleryCatalog.Create(new GalleryConfiguration
    {
        Types = [new GalleryTypeRegistration { TypeId = "cg", ResourceTypeName = "sprite" }],
        Items = [new GalleryItem { Id = "opening", TypeId = "cg", ResourceId = "asset-opening" }]
    });
}
