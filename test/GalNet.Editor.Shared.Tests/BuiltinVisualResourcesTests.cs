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
                new ProjectFileRequest(ProjectFileAction.Move, ["Buildin/Effects/Default"], "Assets"),
                new ProjectFileRequest(ProjectFileAction.Delete, [DefaultParticlePresets.Path]),
                new ProjectFileRequest(ProjectFileAction.Move, ["Buildin/Presets"], "Assets") })
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
    public void WeatherIsExpressedAsFiveOrdinaryParticlePresets()
    {
        using var json = JsonDocument.Parse(DefaultParticlePresets.Create());
        var presets = json.RootElement.GetProperty("presets").EnumerateArray().ToArray();
        Assert.That(presets, Has.Length.EqualTo(5));
        var kinds = new[] { "Rain", "Snow", "Petals", "Dust", "Fireflies" };
        for (var index = 0; index < presets.Length; index++)
        {
            var preset = presets[index];
            Assert.That(preset.GetProperty("type").GetString(), Is.EqualTo(BuiltinEntryIds.PlayParticleEmitter));
            var particle = preset.GetProperty("parameters").GetProperty("parameters");
            var definition = GalNet.Contracts.Builtins.Scene.ParticleEmitterDefinition.FromJson(particle.GetRawText());
            Assert.That(definition.ParticleTexture, Is.EqualTo(BuiltinVisualResources.WeatherTextures[kinds[index]]));
            Assert.That(definition.InitialModule.Lifetime.Min, Is.GreaterThan(0));
            Assert.That(definition.EmissionRate * definition.InitialModule.Lifetime.Max, Is.LessThan(definition.MaxParticles));
            Assert.That(definition.Shape!.Y, Is.EqualTo(index >= 3 ? 540 : -16));
            Assert.That(definition.Shape.Height, Is.EqualTo(index >= 3 ? 1080 : 1));
            Assert.That(definition.Shape.X - definition.Shape.Width / 2, Is.LessThanOrEqualTo(0));
            Assert.That(definition.Shape.X + definition.Shape.Width / 2, Is.GreaterThanOrEqualTo(1920));
            Assert.That(definition.LifetimeModule.Opacity![1].Time * definition.InitialModule.Lifetime.Max, Is.LessThan(.25f));
            if (index == 0) { Assert.That(definition.InitialModule.Size.Max, Is.EqualTo(.143f)); Assert.That(definition.InitialModule.Color.Max, Is.EqualTo("#9FC6DF78")); }
            if (index == 2) Assert.That(definition.InitialModule.Color, Is.EqualTo(new GalNet.Contracts.Builtins.Scene.ParticleColorRange("#ED4786CC", "#FFB1D1F2")));
        }
        Assert.That(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()).TryGet("preset.weather", out _), Is.False);
    }
}
