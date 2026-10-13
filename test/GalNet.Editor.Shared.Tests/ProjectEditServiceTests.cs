using System.Text.Json;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GeneralTest.Editor.Shared;

namespace GalNet.Editor.Shared.Tests;

public sealed class ProjectEditServiceTests
{
    private string _root = null!;
    private ProjectSchemaContext _schema = null!;
    private ProjectEditService _service = null!;
    private string _opening = null!;
    [SetUp]
    public async Task SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "GalNet-edit-" + Guid.NewGuid());
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        await EditorProjectCreator.CreateAsync(new(_root, "Original", Path.Combine(directory.FullName, "test/Fixtures/SchemaA/bin", configuration, "net10.0")));
        _schema = ProjectSchemaContext.Load(_root);
        _service = new(_root, _schema.Catalog);
        using var graph = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json")));
        _opening = graph.RootElement.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("type").GetString() == "Group").GetProperty("id").GetString()!;
    }
    [TearDown]
    public void TearDown() { _schema.Dispose(); Directory.Delete(_root, true); }

    [Test]
    public async Task RealCli_GroupLifecycleAndProjectRename_PreserveIdentityAndSynchronizeFiles()
    {
        var creation = await CliHelpTests.Invoke(["group", "create", _root, "--id", "next", "--name", "Next", "--x", "-12.5"]);
        Assert.That(creation.ExitCode, Is.Zero, creation.Output);
        var groupRename = await CliHelpTests.Invoke(["group", "rename", _root, "--id", "next", "--name", "Renamed"]);
        Assert.That(groupRename.ExitCode, Is.Zero, groupRename.Output);
        Assert.That(File.Exists(Path.Combine(_root, "Graph/groups/next.rawgalgroup")), Is.True);
        var rename = await CliHelpTests.Invoke(["project", "rename", _root, "--name", "Display"]);
        Assert.That(rename.ExitCode, Is.Zero, rename.Output);
        using (var graph = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json"))))
        {
            Assert.That(graph.RootElement.GetProperty("name").GetString(), Is.EqualTo("Display"));
            Assert.That(graph.RootElement.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("id").GetString() == "next").GetProperty("file").GetString(), Is.EqualTo("groups/next.rawgalgroup"));
        }
        var deletion = await CliHelpTests.Invoke(["group", "delete", _root, "--id", "next"]);
        Assert.That(deletion.ExitCode, Is.Zero, deletion.Output);
        Assert.That(File.Exists(Path.Combine(_root, "Graph/groups/next.rawgalgroup")), Is.False);
        Assert.That(new ProjectValidationService(_root).Validate().Success, Is.True);
    }

    [Test]
    public async Task LifecycleDryRuns_DoNotWrite_AndGroupRoutesRejectOtherNodeTypes()
    {
        var original = Snapshot();
        foreach (var args in new[]
        {
            new[] { "group", "create", _root, "--name", "Dry", "--dry-run" },
            new[] { "group", "rename", _root, "--id", _opening, "--name", "Dry", "--dry-run" },
            new[] { "group", "delete", _root, "--id", _opening, "--dry-run" },
            new[] { "project", "rename", _root, "--name", "Dry", "--dry-run" }
        })
        {
            var result = await CliHelpTests.Invoke(args);
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            AssertSnapshot(original);
        }
        using var graph = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json")));
        var entry = graph.RootElement.GetProperty("rootNodeId").GetString()!;
        var rejected = await _service.ExecuteGroupAsync(new RenameNodeCommand(entry, "Wrong"));
        Assert.That(rejected.Success, Is.False);
        AssertSnapshot(original);
    }

    [Test]
    public async Task ExternalReferenceCandidates_RequireExplicitForce_AndAreReported()
    {
        File.WriteAllText(Path.Combine(_root, "references.json"), JsonSerializer.Serialize(new { target = _opening }));
        var original = Snapshot();
        var denied = await _service.ExecuteAsync([new DeleteNodeCommand(_opening)]);
        Assert.That(denied.Success, Is.False);
        Assert.That(denied.Diagnostics.Any(item => item.Code == "references.forceRequired"), Is.True);
        AssertSnapshot(original);
        var dry = await _service.ExecuteGroupAsync(new DeleteNodeCommand(_opening), dryRun: true);
        Assert.That(((ProjectEditReport)dry.Data!).References.Single().File, Is.EqualTo("references.json"));
        AssertSnapshot(original);
        var forced = await _service.ExecuteGroupAsync(new DeleteNodeCommand(_opening, ForceReferences: true));
        Assert.That(forced.Success, Is.True, JsonSerializer.Serialize(forced.Diagnostics));
        Assert.That(((ProjectEditReport)forced.Data!).ReferencesComplete, Is.False);
        Assert.That(File.ReadAllText(Path.Combine(_root, "references.json")), Does.Contain(_opening));
    }

    [Test]
    public async Task ReferencesCreatedEarlierInTheBatch_AreCheckedAgainstWorkingDocument()
    {
        var original = Snapshot();
        var result = await _service.ExecuteAsync([
            new CreateNodeCommand("other", EditorNodeKind.LinearGroup, "Other", 0, 0),
            new AddEntryCommand("other", "ref", Type: "dialogue.text", Parameters: new Dictionary<string, string> { ["content"] = _opening }),
            new DeleteNodeCommand(_opening)
        ]);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(item => item.Code == "references.forceRequired"), Is.True);
        AssertSnapshot(original);
    }

    [TestCase("{\"commandId\":\"project.rename\",\"name\":\"New\",\"unknown\":true}")]
    [TestCase("{\"commandId\":\"project.rename\"}")]
    [TestCase("{\"commandId\":\"project.rename\",\"name\":null}")]
    [TestCase("{\"commandId\":\"project.rename\",\"name\":\"A\",\"name\":\"B\"}")]
    [TestCase("{\"commands\":[],\"unknown\":true}")]
    [TestCase("{\"commands\":{}}")]
    [TestCase("{\"commandId\":\"Project.Rename\",\"name\":\"New\"}")]
    public async Task InvalidJsonArguments_AreRejectedBeforeAnyMutation(string json)
    {
        var original = Snapshot();
        var result = await _service.ExecuteJsonAsync(json);
        Assert.That(result.Success, Is.False);
        AssertSnapshot(original);
    }

    [Test]
    public async Task BatchFailure_PreservesAllFiles_AndFileCommandsNeedNoDocumentLoad()
    {
        var original = Snapshot();
        var failed = await _service.ExecuteAsync([new RenameProjectCommand("Changed"), new RenameNodeCommand("missing", "Missing")]);
        Assert.That(failed.Success, Is.False);
        AssertSnapshot(original);
        File.WriteAllText(Path.Combine(_root, "Graph/graph.json"), "broken");
        var file = await _service.ExecuteJsonAsync("""{"commandId":"asset.directory.create","parentDirectory":"","name":"Created"}""");
        Assert.That(file.Success, Is.True, JsonSerializer.Serialize(file.Diagnostics));
        Assert.That(Directory.Exists(Path.Combine(_root, "Assets/Created")), Is.True);
    }

    [Test]
    public async Task RealCli_InvalidExecuteArguments_ReturnUsageExitCodeWithoutWrites()
    {
        var file = Path.Combine(_root, "command.json");
        File.WriteAllText(file, """{"commandId":"project.rename","name":"New","unknown":true}""");
        var original = Snapshot();
        var result = await CliHelpTests.Invoke(["execute", _root, file]);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Output);
        AssertSnapshot(original);
    }

    [Test]
    public void Help_HasOneProjectRenameDescriptor_WithCliAndDocumentSchemas()
    {
        var catalog = new ToolOperationCatalog();
        Assert.That(catalog.Definitions.Count(item => item.OperationId == "project.rename"), Is.EqualTo(1));
        var rename = catalog.Find("project.rename")!;
        Assert.That(rename.CommandPath, Is.EqualTo("project rename"));
        Assert.That(rename.DocumentInputSchema!.Value.GetProperty("properties").GetProperty("commandId").GetProperty("const").GetString(), Is.EqualTo("project.rename"));
        Assert.That(catalog.Find("group.delete")!.SupportsDryRun, Is.True);
    }

    private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
    private void AssertSnapshot(Dictionary<string, byte[]> expected)
    {
        var actual = Snapshot();
        Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
        foreach (var (file, bytes) in expected) Assert.That(actual[file], Is.EqualTo(bytes), file);
    }
}
