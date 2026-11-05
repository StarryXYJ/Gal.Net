using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Configuration;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shared.Visuals;

namespace GalNet.Editor.Shared.Tests;

public sealed class DefaultVisualTemplateTests
{
    [Test]
    public async Task NewProjectsUseFixedIdsAndInstallPreservesEditedContent()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var contracts = Path.Combine(root.FullName, "src/Editor/GalNet.Editor.DefaultContracts/bin", configuration, "net10.0");
        var parent = Path.Combine(root.FullName, "output", "builtin-project-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = Path.Combine(parent, "first"); var second = Path.Combine(parent, "second");
            var dry = await EditorProjectCreator.CreateAsync(new(first, ContractsDirectory: contracts, DryRun: true));
            Assert.That(Directory.Exists(parent), Is.False);
            await EditorProjectCreator.CreateAsync(new(first, ContractsDirectory: contracts));
            await EditorProjectCreator.CreateAsync(new(second, ContractsDirectory: contracts));
            foreach (var (purpose, name) in DefaultVisualTemplate.ProgramNames)
            {
                var relative = $"Buildin/Effects/Default/{name}.sksl";
                Assert.That(((ProjectCreateReport)dry.Data!).Files, Does.Contain(relative));
                foreach (var project in new[] { first, second })
                {
                    var shader = Path.Combine(project, relative);
                    Assert.That(File.ReadAllText(shader), Is.EqualTo(DefaultVisualTemplate.ReadShader(name)));
                    using var meta = JsonDocument.Parse(File.ReadAllBytes(shader + ".meta"));
                    Assert.That(meta.RootElement.GetProperty("id").GetString(), Is.EqualTo(BuiltinVisualResources.Programs[purpose]));
                }
            }
            var bloom = Path.Combine(first, "Buildin/Effects/Default/Bloom.sksl");
            File.AppendAllText(bloom, "\n// edited\n");
            var edited = File.ReadAllBytes(bloom);
            using var schema = ProjectSchemaContext.Load(first);
            var repeat = await DefaultVisualInstaller.InstallAsync(first, schema.Catalog, schema.Configurations);
            Assert.That(repeat.Success, Is.True, string.Join(", ", repeat.Diagnostics.Select(item => item.Message)));
            Assert.That(File.ReadAllBytes(bloom), Is.EqualTo(edited));
            File.Delete(bloom + ".meta");
            var validation = new ProjectValidationService(first).Validate(structureOnly: true);
            Assert.That(validation.Success, Is.False);
            Assert.That(validation.Diagnostics.Any(item => item.Code == "asset.builtinMissing"), Is.True);
            var buildError = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new ProjectContentBuilder(schema.Catalog).BuildAsync(first));
            Assert.That(buildError!.Message, Does.Contain("Buildin/Effects/Default/Bloom.sksl"));
            var broken = await DefaultVisualInstaller.InstallAsync(first, schema.Catalog, schema.Configurations);
            Assert.That(broken.Success, Is.False);
            Assert.That(File.ReadAllBytes(bloom), Is.EqualTo(edited));
        }
        finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
    }

    [Test]
    public void PreparingTemplateDoesNotMutateProjectConfiguration()
    {
        var configuration = new ProjectConfiguration();
        configuration.SetRaw("custom", new(1, JsonSerializer.SerializeToElement(new { author = "kept" })));
        var plan = DefaultVisualTemplate.Prepare(configuration);
        Assert.That(plan.Configuration.Sections["custom"].Data.GetProperty("author").GetString(), Is.EqualTo("kept"));
        Assert.That(plan.Files.Keys, Has.All.StartsWith("Buildin/"));
    }
}
