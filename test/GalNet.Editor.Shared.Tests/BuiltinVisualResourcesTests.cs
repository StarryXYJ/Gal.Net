using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Shared.Visuals;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Core.Compilation;
using GalNet.Core.Serialization;

namespace GalNet.Editor.Shared.Tests;

public sealed class BuiltinVisualResourcesTests
{
    [Test]
    public async Task BuiltinsCannotBeRemovedOrMovedButCanBeCopiedWithNewIdentity()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
        var root = Path.Combine(directory.FullName, "output", "builtin-ops-" + Guid.NewGuid().ToString("N"));
        try
        {
            await EditorProjectCreator.CreateAsync(new(root));
            foreach (var (path, bytes) in DefaultVisualTemplate.Prepare(new()).Files)
            { var target = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes); }
            var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
            var persistence = new DirectProjectPersistence(root, new EditorDocumentRepository(catalog));
            var document = persistence.Load();
            foreach (var request in new[] {
                new ProjectFileRequest(ProjectFileAction.Delete, ["Buildin/Effects/Default/Bloom.sksl"]),
                new ProjectFileRequest(ProjectFileAction.Move, ["Buildin/Effects/Default"], "Assets") })
            {
                using var operation = new ProjectFileOperation(root, request, catalog);
                Assert.ThrowsAsync<EditorOperationException>(() => operation.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare)));
            }
            using var copy = new ProjectFileOperation(root, new(ProjectFileAction.Copy, ["Buildin/Effects/Default/Bloom.sksl"], "Assets"), catalog);
            await copy.ApplyAsync(true, prepare => persistence.PublishStructureAsync(document, prepare));
            using var meta = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "Assets/Bloom.sksl.meta")));
            Assert.That(meta.RootElement.GetProperty("id").GetString(), Is.Not.EqualTo(BuiltinVisualResources.Programs["bloom"]));
            const string builtin = "Buildin/Effects/Default/Bloom.sksl";
            string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, path))));
            var manifest = new ProjectApplyManifest { Version = 1, Writes = [], Deletes = [
                new() { Path = builtin, ExpectedHash = Hash(builtin) },
                new() { Path = builtin + ".meta", ExpectedHash = Hash(builtin + ".meta") }
            ] };
            var manifestPath = Path.Combine(root, "attempt.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var result = await new ProjectApplyService(root, catalog).ApplyAsync(manifestPath);
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(Path.Combine(root, builtin)), Is.True);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Test]
    public void TemplatesAndPresetDefaultsUseStableIdentitiesWithoutProjectSettings()
    {
        var ids = BuiltinVisualResources.Programs.Values.Concat(BuiltinVisualResources.WeatherTextures.Values).ToArray();
        Assert.That(ids, Has.Length.EqualTo(21));
        Assert.That(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(ids.Length));
        Assert.That(ids.All(id => Guid.TryParse(id, out _)), Is.True);
        var first = DefaultVisualTemplate.Prepare(new());
        var second = DefaultVisualTemplate.Prepare(new());
        Assert.That(first.Files.Keys, Is.EquivalentTo(second.Files.Keys));
        foreach (var path in first.Files.Keys.Where(path => path.EndsWith(".meta", StringComparison.Ordinal)))
        {
            using var a = JsonDocument.Parse(first.Files[path]);
            using var b = JsonDocument.Parse(second.Files[path]);
            Assert.That(a.RootElement.GetProperty("id").GetString(), Is.EqualTo(b.RootElement.GetProperty("id").GetString()));
            Assert.That(path, Does.StartWith("Buildin/"));
        }
        var parameters = VisualPresetBindings.Fill(BuiltinEntryIds.MaskPreset, [], null);
        Assert.That(parameters["program"].GetString(), Is.EqualTo(BuiltinVisualResources.Programs["shapeMask"]));
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var entry = catalog.Create(BuiltinEntryIds.MaskPreset);
        Assert.That(entry.Arguments.GetProperty("program").GetString(), Is.EqualTo(BuiltinVisualResources.ShapeMask));
        parameters["program"] = JsonSerializer.SerializeToElement("custom");
        Assert.That(VisualPresetBindings.Fill(BuiltinEntryIds.MaskPreset, parameters, null)["program"].GetString(), Is.EqualTo("custom"));
    }

    [Test]
    public void CompilerUsesModeSpecificBuiltinTextureWhenAuthorOmitsIt()
    {
        var raw = JsonSerializer.SerializeToElement(new { playbackHandleId = "snow", weather = "Snow" });
        var group = new GroupDocument { Entries = [new() { Id = "weather", Type = BuiltinEntryIds.WeatherPreset,
            Parameters = raw.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone()) }] };
        var compiled = GalgroupCompiler.Compile(group, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).Document.Entries.Single();
        var texture = compiled.Arguments.GetProperty("timeline").GetProperty("events")[0].GetProperty("parameters").GetProperty("parameters").GetProperty("particleTexture").GetString();
        Assert.That(texture, Is.EqualTo(BuiltinVisualResources.WeatherTextures["Snow"]));
    }
}
