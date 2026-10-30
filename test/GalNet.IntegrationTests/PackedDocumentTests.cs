using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Entry;
using GalNet.Core.I18n;
using GalNet.Core.Serialization;
using GalNet.Editor.Shared.Services;
using GalNet.GameHost.Content;
using GalNet.Sample.Avalonia;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class PackedDocumentTests
{
    private string _root = null!;
    private string Source => Path.Combine(_root, "source");
    private string Installed => Path.Combine(_root, "pak");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "galnet-packed-" + Guid.NewGuid().ToString("N"));
        Put(Source, "Graph/graph.json", """{"version":2,"name":"Packed","rootNodeId":"main","nodes":[{"id":"main","name":"Main","type":"Group","file":"groups/main.rawgalgroup"}],"edges":[],"playerVariables":[],"saveVariables":[]}""");
        Put(Source, "Graph/groups/main.rawgalgroup", """{"version":2,"kind":"Raw","entries":[{"id":"first","type":"dialogue.text","parameters":{"content":"line","speaker":"guide"}}]}""");
        Put(Source, "settings.json", JsonSerializer.Serialize(new DefaultProjectOptions { TargetLocale = I18nLocale.EnUs, AvailableLocales = [I18nLocale.EnUs] }.ToConfiguration()));
        Put(Source, "I18n/catalog/characters.json", """[{"id":"guide","displayNameKey":"character.guide.name"}]""");
        Put(Source, "I18n/en-US/a.json", """{"line":"first","character":{"guide":{"name":"Guide"}},"fallback":"Default","empty":""}""");
        Put(Source, "I18n/en-US/groups/z.json", """{"line":"last"}""");
        Put(Source, "I18n/zh/main.json", """{"line":"父语言"}""");
        Put(Source, "I18n/External/en-US/private.json", "broken external author file must not enter the package");
        Put(Source, "I18n/en-US/README.txt", "Author notes are not runtime text sources.");
    }

    [TearDown] public void Cleanup() => Directory.Delete(_root, true);

    private async Task Export()
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        var result = await GamePackageExporter.ExportDirectoryAsync("packed-project", "Packed", Source, Installed, types,
            BuiltinGalleryTypes.CreateCatalog(types), new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()), DefaultProjectConfiguration.Schema);
        Assert.That(result.Success, Is.True, result.Error);
    }

    private async Task<GameContent> Load()
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        return await (await GameInstallation.OpenAsync(Installed)).CreateContentProvider(types, BuiltinGalleryTypes.CreateCatalog(types),
            new() { Configurations = DefaultProjectConfiguration.Schema, SelectDefaultCulture = configuration => configuration.ReadDefaultOptions().TargetLocale.Code }).LoadAsync();
    }

    [Test]
    public async Task BuiltInDocumentsAndLanguagesArePackedWithDeterministicMerging()
    {
        await Export();
        var packed = PackedGameDocuments.ReadGame(File.ReadAllBytes(Path.Combine(Installed, "game.pak")));
        Assert.That(packed.Manifest.Version, Is.EqualTo(3));
        Assert.That(Directory.GetFiles(Installed, "*.json", SearchOption.AllDirectories), Is.Empty);
        Assert.That(Directory.GetFiles(Installed, "*.galnet", SearchOption.AllDirectories), Is.Empty);
        var languages = PackedGameDocuments.Read(File.ReadAllBytes(Path.Combine(Installed, "localization.pak")));
        Assert.That(languages.Documents.Keys, Is.EquivalentTo(new[] { "texts/en-US.json", "texts/zh.json" }));
        var content = await Load();
        Assert.That(content.Localization.Sources, Has.Count.EqualTo(2));
        Assert.That(content.Localization.Characters.GetRequired("guide").DisplayNameKey, Is.EqualTo("character.guide.name"));
        var culture = CultureInfo.GetCultureInfo("en-US");
        var resolver = content.Localization.CreateResolver(() => culture);
        Assert.That(resolver.Resolve("line"), Is.EqualTo("last"));
        Assert.That(resolver.Resolve("character.guide.name"), Is.EqualTo("Guide"));
        Assert.That(resolver.Resolve("empty"), Is.Empty);
        culture = CultureInfo.GetCultureInfo("zh-CN");
        Assert.That(resolver.Resolve("line"), Is.EqualTo("父语言"));
        Assert.That(resolver.Resolve("fallback"), Is.EqualTo("Default"));
        var moved = Path.Combine(_root, "moved");
        var originalProfile = GameLaunchOptions.GetDefaultProfileDirectory(Installed);
        Directory.Move(Installed, moved);
        Assert.That(GameLaunchOptions.GetDefaultProfileDirectory(moved), Is.EqualTo(originalProfile));
        await Export();
        Assert.That(File.ReadAllBytes(Path.Combine(Installed, "game.pak")), Is.EqualTo(File.ReadAllBytes(Path.Combine(moved, "game.pak"))));
        Assert.That(GameLaunchOptions.GetDefaultProfileDirectory(Installed), Is.EqualTo(GameLaunchOptions.GetDefaultProfileDirectory(moved)));
    }

    [Test]
    public async Task AuthorJsonWithByteOrderMarksIsNormalizedToPackedUtf8()
    {
        foreach (var relative in new[] { "I18n/catalog/characters.json", "I18n/en-US/a.json" })
        {
            var path = Path.Combine(Source, relative);
            var text = File.ReadAllText(path);
            File.WriteAllText(path, text, relative.EndsWith("a.json", StringComparison.Ordinal) ? new UTF8Encoding(true) : Encoding.Unicode);
        }
        await Export();
        var content = await Load();
        Assert.That(content.Localization.CreateResolver(() => CultureInfo.GetCultureInfo("en-US")).Resolve("character.guide.name"), Is.EqualTo("Guide"));
    }

    [Test]
    public async Task ExternalFilesPartiallyOverrideAddCulturesAndSkipInvalidSources()
    {
        await Export();
        Put(Installed, "I18n/en-US/a.json", """{"line":"external first"}""");
        Put(Installed, "I18n/en-US/z.json", """{"line":"external last"}""");
        Put(Installed, "I18n/fr-FR/main.json", """{"line":"Bonjour"}""");
        Put(Installed, "I18n/en-US/broken.json", "broken JSON");
        Put(Installed, "I18n/en-US/nonstring.json", """{"line":7}""");
        var content = await Load();
        Assert.That(content.Localization.Diagnostics, Has.Count.EqualTo(2));
        Assert.That(content.Localization.Sources, Has.Count.EqualTo(2));
        var culture = CultureInfo.GetCultureInfo("en-US");
        var resolver = content.Localization.CreateResolver(() => culture);
        Assert.That(resolver.Resolve("line"), Is.EqualTo("external last"));
        Assert.That(resolver.Resolve("fallback"), Is.EqualTo("Default"));
        culture = CultureInfo.GetCultureInfo("fr-FR");
        Assert.That(resolver.Resolve("line"), Is.EqualTo("Bonjour"));
        Assert.That(resolver.AvailableCultures.Select(item => item.Name), Does.Contain("fr-FR"));
        Assert.That(content.Localization.Characters.GetRequired("guide").DisplayNameKey, Is.EqualTo("character.guide.name"));
    }

    [Test]
    public async Task ExternalTextCannotCompleteAnInvalidBuiltInDefaultLanguage()
    {
        await Export();
        var gamePath = Path.Combine(Installed, "game.pak");
        var game = PackedGameDocuments.ReadGame(File.ReadAllBytes(gamePath));
        var languages = PackedGameDocuments.Read(File.ReadAllBytes(Path.Combine(Installed, "localization.pak"))).Documents.ToDictionary();
        var text = JsonSerializer.Deserialize<Dictionary<string, string>>(languages["texts/en-US.json"])!;
        text.Remove("line");
        languages["texts/en-US.json"] = JsonSerializer.SerializeToUtf8Bytes(text);
        var bytes = PackedGameDocuments.Build(game.Manifest.ProjectId, game.Manifest.BuildId, languages);
        File.WriteAllBytes(Path.Combine(Installed, "localization.pak"), bytes);
        var updated = game.Manifest.LocalizationPak! with { Size = bytes.LongLength, Sha256 = CryptoHelper.HashSHA256(bytes) };
        var documents = game.Documents.Documents.ToDictionary();
        documents[GameContentManifest.FileName] = JsonSerializer.SerializeToUtf8Bytes(game.Manifest with { LocalizationPak = updated }, JsonOptions);
        documents[PackedGameDocuments.InstallationPath] = JsonSerializer.SerializeToUtf8Bytes(game.Installation with
        { Files = game.Installation.Files.Select(file => file.Path == "localization.pak" ? updated : file).ToArray() }, JsonOptions);
        File.WriteAllBytes(gamePath, PackedGameDocuments.Build(game.Manifest.ProjectId, game.Manifest.BuildId, documents));
        Put(Installed, "I18n/en-US/complete.json", """{"line":"external cannot repair built-in content"}""");
        var error = Assert.ThrowsAsync<InvalidDataException>(async () => await Load());
        Assert.That(error!.Message, Does.Contain("missing required localization key 'line'"));
    }

    [TestCase("game.pak")]
    [TestCase("localization.pak")]
    public async Task CorruptedPackedDocumentsAreRejectedWithoutLegacyFallback(string file)
    {
        await Export();
        var path = Path.Combine(Installed, file);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 1;
        File.WriteAllBytes(path, bytes);
        Put(Installed, "settings.json", "{}");
        Assert.DoesNotThrow(() => GameLaunchOptions.GetDefaultProfileDirectory(Installed));
        Assert.ThrowsAsync<InvalidDataException>(async () => await Load());
    }

    [TestCase("identity")]
    [TestCase("index")]
    [TestCase("extra")]
    [TestCase("old-version")]
    public async Task InternalDescriptorAndInstallationIndexMustAgree(string change)
    {
        await Export();
        var original = PackedGameDocuments.ReadGame(File.ReadAllBytes(Path.Combine(Installed, "game.pak")));
        var documents = original.Documents.Documents.ToDictionary();
        if (change == "old-version")
            documents[PackedGameDocuments.InstallationPath] = JsonSerializer.SerializeToUtf8Bytes(original.Installation with { Version = 3 }, JsonOptions);
        else if (change == "index")
            documents[PackedGameDocuments.InstallationPath] = JsonSerializer.SerializeToUtf8Bytes(original.Installation with { Files = [] }, JsonOptions);
        else if (change == "extra") documents["unexpected.json"] = "{}"u8.ToArray();
        var bytes = PackedGameDocuments.Build(change == "identity" ? "another-project" : original.Manifest.ProjectId, original.Manifest.BuildId, documents);
        Assert.Throws<InvalidDataException>(() => PackedGameDocuments.ReadGame(bytes));
    }

    [Test]
    public async Task ArchiveInstallationPreservesEmptyDirectoriesAndExistingExternalTranslation()
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        var result = await GamePackageExporter.ExportAsync("packed-project", "Packed", Source, Path.Combine(_root, "packages"), types,
            BuiltinGalleryTypes.CreateCatalog(types), new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()), DefaultProjectConfiguration.Schema);
        Assert.That(result.Success, Is.True, result.Error);
        var installed = await GalpakInstaller.InstallAsync(result.PackagePath!);
        var descriptor = InstalledGameDescriptor.Read(installed);
        foreach (var directory in PackedGameDocuments.ExtensionDirectories(descriptor.BuildId))
            Assert.That(Directory.Exists(Path.Combine(installed, directory)), Is.True, directory);
        var extracted = Path.Combine(_root, "extracted");
        ZipFile.ExtractToDirectory(result.PackagePath!, extracted);
        foreach (var directory in PackedGameDocuments.ExtensionDirectories(descriptor.BuildId))
            Assert.That(Directory.Exists(Path.Combine(extracted, "pak", directory)), Is.True, directory);
        Put(installed, "I18n/en-US/community.json", """{"line":"preserved"}""");
        Assert.That(await GalpakInstaller.InstallAsync(result.PackagePath!), Is.EqualTo(installed));
        Assert.That(File.ReadAllText(Path.Combine(installed, "I18n/en-US/community.json")), Does.Contain("preserved"));
        var installedGame = Path.Combine(installed, "game.pak");
        var originalGame = File.ReadAllBytes(installedGame);
        var damagedGame = originalGame.ToArray();
        damagedGame[^1] ^= 1;
        File.WriteAllBytes(installedGame, damagedGame);
        Assert.ThrowsAsync<InvalidOperationException>(() => GalpakInstaller.InstallAsync(result.PackagePath!));
        Assert.That(File.ReadAllBytes(installedGame), Is.EqualTo(damagedGame));
        File.WriteAllBytes(installedGame, originalGame);
        using (var zip = ZipFile.Open(result.PackagePath!, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("pak/localization.pak")!;
            using var stream = entry.Open();
            stream.Position = stream.Length - 1;
            var previous = stream.ReadByte();
            stream.Position--;
            stream.WriteByte((byte)(previous ^ 1));
        }
        Assert.ThrowsAsync<InvalidDataException>(() => GalpakInstaller.InstallAsync(result.PackagePath!, installed));
        Assert.That(File.ReadAllBytes(installedGame), Is.EqualTo(originalGame));
        Assert.ThrowsAsync<InvalidDataException>(() => GalpakInstaller.InstallAsync(result.PackagePath!, Path.Combine(_root, "broken")));
        Assert.That(Directory.Exists(Path.Combine(_root, "broken")), Is.False);
    }

    private static void Put(string root, string path, string text)
    {
        var destination = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, text);
    }
}
