using System.Text.Json;
using DynamicLocalization.Core;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
using GalNet.Core.Graph;
using GalNet.Core.Settings;
using GalNet.Runtime.Content;
using GalNet.Sample.Avalonia.Services;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class GameLocalizationLoadingTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "galnet-i18n-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "I18n", "en-US", "groups"));
        Directory.CreateDirectory(Path.Combine(_root, "I18n", "catalog"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public void RecursiveSourcesRespectOrderParentAndGameDefaultFallback()
    {
        File.WriteAllText(Path.Combine(_root, "I18n", "en-US", "a.json"), """{"line":"first","fallback":"Default","same":"same"}""");
        File.WriteAllText(Path.Combine(_root, "I18n", "en-US", "groups", "z.json"), """{"line":"last"}""");
        Directory.CreateDirectory(Path.Combine(_root, "I18n", "zh"));
        File.WriteAllText(Path.Combine(_root, "I18n", "zh", "main.json"), """{"line":"父语言"}""");
        Directory.CreateDirectory(Path.Combine(_root, "I18n", "External", "en-US"));
        File.WriteAllText(Path.Combine(_root, "I18n", "External", "en-US", "ignored.json"), "broken JSON excluded from built-in sources");
        var content = new GameContent
        {
            Graph = new(),
            Settings = new() { TargetLocale = GalNet.Core.I18n.I18nLocale.EnUs },
            Localization = GameLocalizationLoader.Load(_root, new(), new() { TargetLocale = GalNet.Core.I18n.I18nLocale.EnUs })
        };
        var cultures = new CultureService();
        using var resolver = new SampleLocalizationService(cultures);
        resolver.AttachGame(content);
        cultures.SetCulture("en-US");
        Assert.That(resolver.Resolve("line"), Is.EqualTo("last"));
        cultures.SetCulture("zh-CN");
        Assert.Multiple(() =>
        {
            Assert.That(resolver.Resolve("line"), Is.EqualTo("父语言"));
            Assert.That(resolver.Resolve("fallback"), Is.EqualTo("Default"));
            Assert.That(resolver.Resolve("missing"), Is.EqualTo("missing"));
            Assert.That(resolver.Resolve("same"), Is.EqualTo("same"));
        });
        resolver.AttachGame(new GameContent { Graph = new() });
        Assert.That(resolver.Resolve("fallback"), Is.EqualTo("fallback"), "Replacing a game must not keep the previous translations.");
    }

    [Test]
    public void ExternalSourcesAreSortedAndInvalidFilesDoNotPreventLoading()
    {
        var external = Path.Combine(_root, "I18n", "External", "en-US");
        Directory.CreateDirectory(Path.Combine(external, "nested"));
        File.WriteAllText(Path.Combine(external, "a.json"), """{"line":"first"}""");
        File.WriteAllText(Path.Combine(external, "b.json"), "{broken");
        File.WriteAllText(Path.Combine(external, "c.json"), """{"line":1}""");
        File.WriteAllText(Path.Combine(external, "nested", "z.json"), """{"line":"last"}""");
        var loaded = GameLocalizationLoader.Load(_root, new(), new());
        Assert.That(loaded.ExternalSources.Select(source => source.RelativePath),
            Is.EqualTo(new[] { "External/en-US/a.json", "External/en-US/nested/z.json" }));
        Assert.That(loaded.Diagnostics, Has.Count.EqualTo(2));
        using var resolver = new SampleLocalizationService(new CultureService());
        resolver.AttachGame(new GameContent { Graph = new(), Localization = loaded });
        resolver.ApplyCulture("en-US");
        Assert.That(resolver.Resolve("line"), Is.EqualTo("last"));
    }

    [Test]
    public void SpecificCultureWinsOverLaterParentFileAndInvalidJsonIsRejected()
    {
        Directory.CreateDirectory(Path.Combine(_root, "I18n", "zh-CN"));
        Directory.CreateDirectory(Path.Combine(_root, "I18n", "zh"));
        File.WriteAllText(Path.Combine(_root, "I18n", "zh-CN", "main.json"), """{"line":"specific"}""");
        File.WriteAllText(Path.Combine(_root, "I18n", "zh", "main.json"), """{"line":"parent","parentOnly":"inherited"}""");
        var settings = new ProjectSettings();
        var content = new GameContent { Graph = new(), Settings = settings, Localization = GameLocalizationLoader.Load(_root, new(), settings) };
        using var resolver = new SampleLocalizationService(new CultureService());
        resolver.AttachGame(content);
        resolver.ApplyCulture("zh-CN");
        Assert.That(resolver.Resolve("line"), Is.EqualTo("specific"));
        Assert.That(resolver.Resolve("parentOnly"), Is.EqualTo("inherited"));
        File.WriteAllText(Path.Combine(_root, "I18n", "en-US", "invalid.json"), "{broken");
        Assert.That(() => GameLocalizationLoader.Load(_root, new(), settings),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("invalid.json"));
    }

    [Test]
    public void MissingDefaultKeyAndUnknownCharactersFailDuringContentValidation()
    {
        var graph = new Graph
        {
            Nodes = { new Group { Entries = { new PrimitiveEntry("dialogue.text", JsonSerializer.SerializeToElement(new { content = "required", speaker = "" })) } } }
        };
        Assert.That(() => GameLocalizationLoader.Load(_root, graph, new ProjectSettings()),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("required"));
        File.WriteAllText(Path.Combine(_root, "I18n", "en-US", "main.json"), """{"required":"Present"}""");
        ((Group)graph.Nodes[0]).Entries[0] = new PrimitiveEntry("dialogue.text", JsonSerializer.SerializeToElement(new { content = "required", speaker = "unknown" }));
        Assert.That(() => GameLocalizationLoader.Load(_root, graph, new() { TargetLocale = GalNet.Core.I18n.I18nLocale.EnUs }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("unknown"));
    }

    [TestCase("{}")]
    [TestCase("{\"content\":null,\"speaker\":null}")]
    [TestCase("{\"content\":\"\",\"speaker\":\"\"}")]
    [TestCase("{\"content\":\"   \",\"speaker\":\"   \"}")]
    public void EmptyDialogueNvlAndChoiceTextDoNotRequireLocalizationEntries(string arguments)
    {
        var graph = new Graph
        {
            Nodes =
            {
                new Group { Entries =
                {
                    new PrimitiveEntry("dialogue.text", JsonSerializer.Deserialize<JsonElement>(arguments)),
                    new PrimitiveEntry("nvl.append", JsonSerializer.Deserialize<JsonElement>(arguments))
                } },
                new Branch { BranchType = BranchType.Choice, Options = [new() { Text = "" }, new() { Text = "   " }] }
            }
        };
        Assert.That(() => GameLocalizationLoader.Load(_root, graph, new()), Throws.Nothing);
    }

    [Test]
    public async Task ProjectAndInstalledProvidersReadEquivalentTextSources()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Graph"));
        File.WriteAllText(Path.Combine(_root, "Graph", "graph.json"), """{"nodes":[],"edges":[],"rootNodeId":""}""");
        File.WriteAllText(Path.Combine(_root, "I18n", "catalog", "characters.json"), """[{"id":"guide","displayNameKey":"character.guide.name"}]""");
        Directory.CreateDirectory(Path.Combine(_root, "I18n", "zh-CN"));
        File.WriteAllText(Path.Combine(_root, "I18n", "zh-CN", "characters.json"), """{"character":{"guide":{"name":"向导"}}}""");
        var types = BuiltinResourceTypes.CreateCatalog();
        var galleries = BuiltinGalleryTypes.CreateCatalog(types);
        File.WriteAllText(Path.Combine(_root, "gallery.json"), JsonSerializer.Serialize(
            new GalleryConfiguration { Types = galleries.Types.ToList(), Items = [] }, JsonSerializerOptions.Web));
        var project = await new ProjectGameContentProvider(_root, types, galleries).LoadAsync();
        var installed = await new InstalledGameContentProvider(_root, types, galleries).LoadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(installed.Localization.Sources, Is.EqualTo(project.Localization.Sources));
            Assert.That(installed.Localization.Characters.GetRequired("guide"), Is.EqualTo(project.Localization.Characters.GetRequired("guide")));
        });
        var cultures = new CultureService();
        using var resolver = new SampleLocalizationService(cultures);
        resolver.AttachGame(installed);
        cultures.SetCulture("zh-CN");
        Assert.That(resolver.Resolve("character.guide.name"), Is.EqualTo("向导"));
        Assert.That(project.AssetRoot, Is.EqualTo(Path.Combine(_root, "Assets")), "I18n remains outside the asset root.");
    }
}
