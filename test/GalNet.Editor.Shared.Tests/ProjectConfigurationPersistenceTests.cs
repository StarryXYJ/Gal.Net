using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class ProjectConfigurationPersistenceTests
{
    private string _root = null!;

    [SetUp]
    public async Task SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "GalNet-config-" + Guid.NewGuid().ToString("N"));
        await EditorProjectCreator.CreateAsync(new(_root, "Configuration"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public async Task DefaultEditorPatchKeepsUnownedConfigurationAndRejectsPartialUpdates()
    {
        var persistence = Persistence();
        var document = persistence.Load();
        document.Configuration.SetRaw("developer.options", new(4, JsonSerializer.SerializeToElement(new { enabled = true })));
        document.Configuration.SetRaw(DefaultProjectConfiguration.Section.Id, new(1,
            JsonSerializer.SerializeToElement(new { defaultWidth = 1280, future = new { enabled = true } })));
        var handler = new BuiltInEditorCommandHandler(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
        var result = handler.Execute(document, new PatchProjectSettingsCommand(new Dictionary<string, JsonElement>
        { ["defaultWidth"] = JsonSerializer.SerializeToElement(1440), ["unknown"] = JsonSerializer.SerializeToElement(1) }), new(0, false));
        Assert.That(result.Success, Is.False);
        Assert.That(document.Configuration.ReadDefaultOptions().DefaultWidth, Is.EqualTo(1280));
        result = handler.Execute(document, new PatchProjectSettingsCommand(new Dictionary<string, JsonElement>
        { ["defaultWidth"] = JsonSerializer.SerializeToElement(1440) }), new(0, false));
        Assert.That(result.Success, Is.True);
        await persistence.SaveAsync(document);
        var restored = Persistence().Load().Configuration;
        Assert.That(restored.ReadDefaultOptions().DefaultWidth, Is.EqualTo(1440));
        Assert.That(restored.Sections["developer.options"].Version, Is.EqualTo(4));
        Assert.That(restored.Sections["developer.options"].Data.GetProperty("enabled").GetBoolean(), Is.True);
        Assert.That(restored.Sections[DefaultProjectConfiguration.Section.Id].Data.GetProperty("future").GetProperty("enabled").GetBoolean(), Is.True);
    }

    [Test]
    public void InvalidKnownSectionCannotBePublishedOrLoadedWhenSchemaIsSelected()
    {
        var persistence = Persistence();
        var document = persistence.Load();
        var path = Path.Combine(_root, "settings.json");
        var original = File.ReadAllBytes(path);
        document.Configuration.SetRaw(DefaultProjectConfiguration.Section.Id, new(1, JsonSerializer.SerializeToElement(new { defaultWidth = 0 })));
        Assert.That(() => persistence.PrepareSave(document), Throws.TypeOf<InvalidDataException>());
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        File.WriteAllText(path, JsonSerializer.Serialize(document.Configuration));
        Assert.That(() => Persistence().Load(), Throws.TypeOf<InvalidDataException>());
    }

    private DirectProjectPersistence Persistence() => new(_root,
        new EditorDocumentRepository(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())), DefaultProjectConfiguration.Schema);
}
