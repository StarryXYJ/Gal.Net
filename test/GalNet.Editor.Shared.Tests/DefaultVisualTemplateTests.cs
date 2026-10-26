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
    public void Plan_preserves_author_bindings_and_unknown_configuration_without_mutating_input()
    {
        var configuration = new DefaultProjectOptions { DefaultVisualPrograms = new() { ["blur"] = "custom-blur" } }.ToConfiguration();
        configuration.SetRaw("custom", new(1, JsonSerializer.SerializeToElement(new { author = "kept" })));
        var plan = DefaultVisualTemplate.Prepare(configuration);
        Assert.Multiple(() =>
        {
            Assert.That(configuration.ReadDefaultOptions().DefaultVisualPrograms, Has.Count.EqualTo(1));
            Assert.That(plan.Configuration.ReadDefaultOptions().DefaultVisualPrograms["blur"], Is.EqualTo("custom-blur"));
            Assert.That(plan.Files.ContainsKey("Assets/Effects/Default/Blur.sksl"), Is.False);
            Assert.That(plan.Configuration.Sections["custom"].Data.GetProperty("author").GetString(), Is.EqualTo("kept"));
        });
        Assert.That(DefaultVisualTemplate.Prepare(plan.Configuration).Files, Is.Empty, "Preparing an already bound project must not replace its assets.");
    }

    [Test]
    public async Task Default_project_deploys_editable_sources_with_project_owned_ids_and_accurate_dry_run()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var contracts = Path.Combine(directory.FullName, "src/Editor/GalNet.Editor.DefaultContracts/bin", configuration, "net10.0");
        var parent = Path.Combine(Path.GetTempPath(), "GalNet-visuals-" + Guid.NewGuid());
        try
        {
            var target = Path.Combine(parent, "Content");
            var dry = await EditorProjectCreator.CreateAsync(new(target, ContractsDirectory: contracts, DryRun: true));
            var report = (ProjectCreateReport)dry.Data!;
            Assert.That(Directory.Exists(parent), Is.False);
            Assert.That(report.Files.Count(file => file.EndsWith(".sksl", StringComparison.Ordinal)), Is.EqualTo(8));
            var created = await EditorProjectCreator.CreateAsync(new(target, ContractsDirectory: contracts));
            var createdReport = (ProjectCreateReport)created.Data!;
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "settings.json")));
            var settings = json.RootElement.Deserialize<ProjectConfiguration>()!;
            var bindings = settings.ReadDefaultOptions().DefaultVisualPrograms;
            Assert.That(bindings, Has.Count.EqualTo(8));
            foreach (var (purpose, name) in DefaultVisualTemplate.ProgramNames)
            {
                var shader = Path.Combine(target, "Assets/Effects/Default", name + ".sksl");
                Assert.That(File.ReadAllText(shader), Is.EqualTo(DefaultVisualTemplate.ReadShader(name)));
                using var meta = JsonDocument.Parse(File.ReadAllText(shader + ".meta"));
                Assert.That(meta.RootElement.GetProperty("id").GetString(), Is.EqualTo(bindings[purpose]));
            }
            Assert.That(new ProjectValidationService(target).Validate().Success, Is.True);
            var actual = Directory.GetFiles(target, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(target, path).Replace('\\', '/'));
            Assert.That(actual, Is.EquivalentTo(createdReport.Files));
            Assert.That(createdReport.Files.Where(file => file.StartsWith("Assets/", StringComparison.Ordinal)),
                Is.EquivalentTo(report.Files.Where(file => file.StartsWith("Assets/", StringComparison.Ordinal))));
            var another = DefaultVisualTemplate.Prepare(new ProjectConfiguration()).Configuration.ReadDefaultOptions();
            Assert.That(another.DefaultVisualPrograms.Values.Intersect(bindings.Values), Is.Empty);
        }
        finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
    }
}
