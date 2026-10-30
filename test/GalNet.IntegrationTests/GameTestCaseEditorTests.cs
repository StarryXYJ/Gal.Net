using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Settings;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Runtime;

public sealed class GameTestCaseEditorTests
{
    [Test]
    public async Task SampleOpensEditsSavesReopensAndBuildsWithItsOwnContract()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "GalNet.slnx")))
            repository = repository.Parent;
        Assert.That(repository, Is.Not.Null);
        var source = Path.Combine(repository!.FullName, "GameTestCase");
        var root = Path.Combine(Path.GetTempPath(), "GalNet-sample-editor-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.Copy(Path.Combine(source, "settings.json"), Path.Combine(root, "settings.json"));
            foreach (var folder in new[] { "Graph", "Assets", "I18n" })
                foreach (var file in Directory.EnumerateFiles(Path.Combine(source, folder), "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(root, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }

            // Use the contract built with the Sample, independent of local Contracts/ deployment.
            var contracts = Directory.CreateDirectory(Path.Combine(root, "Contracts")).FullName;
            foreach (var file in new[] { "GalNet.Project.Contracts.dll", "GalNet.Core.dll", "GalNet.Contracts.Builtins.dll" })
                File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, file), Path.Combine(contracts, file));
            var preserved = new[] { "Assets", "I18n" }.SelectMany(folder =>
                    Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories))
                .ToDictionary(file => file, file => SHA256.HashData(File.ReadAllBytes(file)));

            using var services = new ServiceCollection()
                .AddSingleton<IEditorSettingsService, MemorySettings>()
                .AddSingleton<IProjectService, ProjectService>()
                .AddScoped<ProjectSchemaSession>()
                .AddScoped<IEntryCatalog>(provider => provider.GetRequiredService<ProjectSchemaSession>().Catalog)
                .AddScoped<IEditorDocumentRepository>(provider =>
                    new EditorDocumentRepository(provider.GetRequiredService<IEntryCatalog>(), EditorDocumentValidation.Authoring))
                .AddScoped<IEditorDocumentService, EditorDocumentService>()
                .AddScoped<IProjectContentSession, ProjectContentSession>()
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            var projects = services.GetRequiredService<IProjectService>();
            try
            {
                var project = await projects.OpenAsync(root);
                var documents = project.Services.GetRequiredService<IEditorDocumentService>();
                var session = project.Services.GetRequiredService<IProjectContentSession>();
                Assert.That(session.IsDirty, Is.False);
                Assert.That(documents.CurrentDocument.Nodes.Single(node => node.Type == "Entry").Id,
                    Is.EqualTo(documents.CurrentDocument.RootNodeId));
                Assert.That(documents.CurrentDocument.Edges, Has.One.Matches<GalNet.Editor.Abstraction.Documents.EditorGraphEdgeDto>(
                    edge => edge.FromNodeId == documents.CurrentDocument.RootNodeId && edge.ToNodeId == "intro"));
                Assert.That(project.Services.GetRequiredService<IEntryCatalog>().TryGet("sample.screenshot", out _), Is.True);
                documents.CurrentDocument.Name = "Edited sample";
                documents.Groups["intro"].Entries.Single(entry => entry.StableId == "intro-line-1").Condition = "true";
                var expectedGraph = JsonSerializer.Serialize(documents.CurrentDocument);
                var expectedGroups = JsonSerializer.SerializeToElement(documents.LoadedProject.GroupEntries);
                var expectedSettings = JsonSerializer.SerializeToElement(project.Configuration);
                Assert.That(session.IsDirty, Is.True);
                await projects.SaveAsync();
                Assert.That(session.IsDirty, Is.False);
                await projects.CloseAsync();

                var reopened = await projects.OpenAsync(root);
                var reloaded = reopened.Services.GetRequiredService<IEditorDocumentService>();
                Assert.That(JsonSerializer.Serialize(reloaded.CurrentDocument), Is.EqualTo(expectedGraph));
                Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(reloaded.LoadedProject.GroupEntries), expectedGroups),
                    Is.True, "Saving must preserve every Group/Entry ID, type, condition and parameter.");
                Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(reopened.Configuration), expectedSettings), Is.True);
                Assert.That(reopened.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
                foreach (var (file, hash) in preserved)
                    Assert.That(SHA256.HashData(File.ReadAllBytes(file)), Is.EqualTo(hash), file);
                var validation = new ProjectValidationService(root).Validate();
                Assert.That(validation.Success, Is.True, JsonSerializer.Serialize(validation.Diagnostics));
                var built = await new ProjectContentBuilder(reopened.Services.GetRequiredService<IEntryCatalog>())
                    .BuildAsync(root, Path.Combine(root, "Output"));
                Assert.That(built.CompiledGroupCount, Is.EqualTo(4));
            }
            finally { await projects.CloseAsync(); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class MemorySettings : IEditorSettingsService
    {
        private readonly EditorSettings _settings = new() { AutoSaveProject = false };
        public EditorSettings GetSettings() => _settings;
        public void SaveSettings() { }
    }
}
