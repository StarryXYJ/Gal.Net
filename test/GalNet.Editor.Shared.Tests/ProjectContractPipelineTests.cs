using System.IO.Compression;
using System.Text.Json;
using GalNet.Editor.Shared.Services;
using GeneralTest.Editor.Shared;

namespace GalNet.Editor.Shared.Tests;

public sealed class ProjectContractPipelineTests
{
    [Test]
    public async Task ActualCliValidationAndCompilationStillRejectAnAuthoringSavedUnknownType()
    {
        Put("Graph/groups/a.rawgalgroup", """{"version":2,"kind":"Raw","entries":[{"id":"opaque","type":"future.custom","parameters":{"payload":[null,{"kept":true}]}}]}""");
        using var schema = ProjectSchemaContext.Load(_root);
        var repository = new EditorDocumentRepository(schema.Catalog, EditorDocumentValidation.Authoring);
        var loaded = repository.Load(_root, "Draft", new());
        repository.Save(_root, loaded.Document, loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<GalNet.Editor.Abstraction.Documents.EditorEntryData>)pair.Value));
        var original = File.ReadAllBytes(Path.Combine(_root, "Graph/groups/a.rawgalgroup"));
        var validation = await CliHelpTests.Invoke(["validate", _root]);
        var compilation = await CliHelpTests.Invoke(["group", "compile", _root, "--id", "a"]);
        Assert.That(validation.ExitCode, Is.Not.Zero); Assert.That(validation.Output, Does.Contain("entry.typeUnknown"));
        Assert.That(compilation.ExitCode, Is.Not.Zero); Assert.That(compilation.Output, Does.Contain("entry.typeUnknown"));
        Assert.That(File.ReadAllBytes(Path.Combine(_root, "Graph/groups/a.rawgalgroup")), Is.EqualTo(original));
    }
    private string _root = null!;
    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-pipeline-" + Guid.NewGuid())).FullName;
        Put("Graph/graph.json", """{"version":2,"name":"Custom","rootNodeId":"a","nodes":[{"id":"a","name":"A","type":"Group","file":"groups/a.rawgalgroup"}],"edges":[],"playerVariables":[],"saveVariables":[]}""");
        Put("Graph/groups/a.rawgalgroup", """{"version":2,"kind":"Raw","entries":[{"id":"bundle","type":"custom.bundle","parameters":{}}]}""");
        Put("settings.json", "{}");
        Deploy("SchemaA");
    }
    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public async Task CliValidateBuildAndExport_UseTheSameDeployedCustomContract()
    {
        var original = File.ReadAllBytes(Path.Combine(_root, "Graph/groups/a.rawgalgroup"));
        var validate = await CliHelpTests.Invoke(["validate", _root]);
        var build = await CliHelpTests.Invoke(["build", _root]);
        var export = await CliHelpTests.Invoke(["export", _root]);
        Assert.Multiple(() =>
        {
            Assert.That(validate.ExitCode, Is.Zero, validate.Output);
            Assert.That(build.ExitCode, Is.Zero, build.Output);
            Assert.That(export.ExitCode, Is.Zero, export.Output);
            Assert.That(File.ReadAllBytes(Path.Combine(_root, "Graph/groups/a.rawgalgroup")), Is.EqualTo(original));
        });
        using var result = JsonDocument.Parse(export.Output);
        using var zip = ZipFile.OpenRead(result.RootElement.GetProperty("data").GetProperty("packagePath").GetString()!);
        using var entry = zip.GetEntry("Graph/groups/a.galgroup")!.Open();
        using var compiled = JsonDocument.Parse(entry);
        Assert.That(compiled.RootElement.GetProperty("entries")[0].GetProperty("typeId").GetString(), Is.EqualTo("custom.example"));
        Assert.That(zip.Entries.Any(item => item.FullName.StartsWith("Contracts/", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public async Task PartialContract_DoesNotGainBuiltins_AndFailurePreservesOldArtifacts()
    {
        Deploy("SchemaB");
        Put("Graph/groups/a.rawgalgroup", """{"version":2,"kind":"Raw","entries":[{"id":"text","type":"dialogue.text","parameters":{"content":"hello"}}]}""");
        Put("Output/sentinel.txt", "preserve");
        using var schema = ProjectSchemaContext.Load(_root);
        var error = Assert.ThrowsAsync<InvalidDataException>(() => new ProjectContentBuilder(schema.Catalog).BuildAsync(_root));
        Assert.That(error!.Message, Does.Contain("not declared"));
        var export = await CliHelpTests.Invoke(["export", _root]);
        Assert.That(export.ExitCode, Is.EqualTo(5), export.Output);
        Assert.That(File.ReadAllText(Path.Combine(_root, "Output/sentinel.txt")), Is.EqualTo("preserve"));
        Assert.That(Directory.GetFiles(Path.Combine(_root, "Output"), "*.galpak"), Is.Empty);
    }

    [Test]
    public async Task RawInvalidCompositeArguments_AreRejectedBeforeCompilationOrOutputReplacement()
    {
        Put("Graph/groups/a.rawgalgroup", """{"version":2,"kind":"Raw","entries":[{"id":"bundle","type":"custom.bundle","parameters":{"unknown":true}}]}""");
        Put("Output/sentinel.txt", "preserve");
        var result = await CliHelpTests.Invoke(["build", _root]);
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output, Does.Contain("Unknown primitive parameter"));
        Assert.That(File.ReadAllText(Path.Combine(_root, "Output/sentinel.txt")), Is.EqualTo("preserve"));
    }

    [Test]
    public async Task MissingDll_BuildAndExportFailWithoutFallback()
    {
        File.Delete(Path.Combine(_root, "Contracts", ProjectSchemaContext.AssemblyFileName));
        var build = await CliHelpTests.Invoke(["build", _root]);
        var export = await CliHelpTests.Invoke(["export", _root]);
        Assert.That(build.ExitCode, Is.EqualTo(1));
        Assert.That(export.ExitCode, Is.EqualTo(1));
        Assert.That(Directory.Exists(Path.Combine(_root, "Output")), Is.False);
    }

    private void Deploy(string fixture)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var destination = Directory.CreateDirectory(Path.Combine(_root, "Contracts")).FullName;
        foreach (var file in Directory.GetFiles(Path.Combine(directory.FullName, "test", "Fixtures", fixture, "bin", configuration, "net10.0")))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }
    private void Put(string file, string text)
    { var path = Path.Combine(_root, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
}
