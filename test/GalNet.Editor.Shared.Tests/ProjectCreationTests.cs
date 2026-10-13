using System.Text.Json;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GeneralTest.Editor.Shared;

namespace GalNet.Editor.Shared.Tests;

public sealed class ProjectCreationTests
{
    private string _parent = null!;
    private string _target = null!;
    private string _contracts = null!;
    [SetUp]
    public void SetUp()
    {
        _parent = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-create-" + Guid.NewGuid())).FullName;
        _target = Path.Combine(_parent, "Content");
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        _contracts = Path.Combine(directory.FullName, "test/Fixtures/SchemaA/bin", configuration, "net10.0");
    }
    [TearDown]
    public void TearDown() => Directory.Delete(_parent, true);

    [Test]
    public async Task WithoutContract_CreatesOnlyAnEmptyScaffold_WithExplicitUnavailableSemantics()
    {
        var result = await EditorProjectCreator.CreateAsync(new(_target, "Scaffold"));
        Assert.That(result.Success, Is.True);
        var graph = new ProjectValidationService(_target).Validate("graph");
        Assert.That(graph.Success, Is.True, JsonSerializer.Serialize(graph.Diagnostics));
        Assert.That(new ProjectValidationService(_target).Validate(structureOnly: true).Success, Is.True);
        Assert.That(new ProjectValidationService(_target).Validate().Diagnostics.Any(item => item.Code == "contract.load"), Is.True);
        Assert.That(File.Exists(Path.Combine(_target, "Contracts/README.md")), Is.True);
        var group = Directory.GetFiles(Path.Combine(_target, "Graph/groups")).Single();
        using var raw = JsonDocument.Parse(File.ReadAllBytes(group));
        Assert.That(raw.RootElement.GetProperty("entries").GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task ContractDirectory_IsValidatedAndDeployed_AndProjectUsesExactlyThatCatalog()
    {
        var result = await EditorProjectCreator.CreateAsync(new(_target, "Custom", _contracts));
        Assert.That(result.Success, Is.True);
        using var context = ProjectSchemaContext.Load(_target);
        Assert.That(context.Catalog.TryGet("custom.bundle", out _), Is.True);
        Assert.That(context.Catalog.TryGet("audio.play", out _), Is.False);
        var validation = new ProjectValidationService(_target).Validate();
        Assert.That(validation.Success, Is.True, JsonSerializer.Serialize(validation.Diagnostics));
        Assert.That(File.Exists(Path.Combine(_target, "Contracts/SchemaDependency.dll")), Is.True);
        Assert.That(Directory.GetDirectories(_parent, ".galnet-create-*"), Is.Empty);
    }

    [Test]
    public async Task DryRun_ValidatesContract_WithoutCreatingTargetOrMissingParents()
    {
        var target = Path.Combine(_parent, "Missing/Parent/Content");
        var result = await EditorProjectCreator.CreateAsync(new(target, ContractsDirectory: _contracts, DryRun: true));
        Assert.That(result.Success, Is.True);
        Assert.That(Directory.GetFileSystemEntries(_parent), Is.Empty);
    }

    [Test]
    public void InvalidContractAndCancellation_DoNotLeavePartialProjects()
    {
        Assert.ThrowsAsync<FileNotFoundException>(() => EditorProjectCreator.CreateAsync(new(_target, ContractsDirectory: _parent)));
        Assert.That(Directory.GetFileSystemEntries(_parent), Is.Empty);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => EditorProjectCreator.CreateAsync(new(_target, ContractsDirectory: _contracts), cancellationToken: cancellation.Token));
        Assert.That(Directory.GetFileSystemEntries(_parent), Is.Empty);
    }

    [Test]
    public async Task ExistingEmptyTarget_IsSupported_ButNonemptyTargetIsNeverOverwritten()
    {
        Directory.CreateDirectory(_target);
        await EditorProjectCreator.CreateAsync(new(_target));
        var original = File.ReadAllBytes(Path.Combine(_target, "Graph/graph.json"));
        Assert.ThrowsAsync<IOException>(() => EditorProjectCreator.CreateAsync(new(_target, "Overwrite")));
        Assert.That(File.ReadAllBytes(Path.Combine(_target, "Graph/graph.json")), Is.EqualTo(original));
    }

    [Test]
    public async Task RealCli_CreateAddCustomCompositeValidateAndCompile()
    {
        var creation = await CliHelpTests.Invoke(["project", "create", _target, "--name", "CLI Custom", "--contracts", _contracts]);
        Assert.That(creation.ExitCode, Is.Zero, creation.Output);
        using var graph = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_target, "Graph/graph.json")));
        var group = graph.RootElement.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("type").GetString() == "Group").GetProperty("id").GetString()!;
        var command = Path.Combine(_parent, "add.json");
        await File.WriteAllTextAsync(command, JsonSerializer.Serialize(new
        {
            commandId = "group.entry.add",
            groupId = group,
            entryId = "bundle",
            type = "custom.bundle",
            parameters = new { }
        }));
        var edit = await CliHelpTests.Invoke(["execute", _target, command]);
        Assert.That(edit.ExitCode, Is.Zero, edit.Output);
        var validation = await CliHelpTests.Invoke(["validate", _target]);
        Assert.That(validation.ExitCode, Is.Zero, validation.Output);
        var compilation = await CliHelpTests.Invoke(["group", "compile", _target, "--id", group]);
        Assert.That(compilation.ExitCode, Is.Zero, compilation.Output);
    }

    [Test]
    public async Task RealCli_DryRun_ReportsContractAndFiles_WithoutWrites()
    {
        var result = await CliHelpTests.Invoke(["project", "create", _target, "--contracts", _contracts, "--dry-run"]);
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.That(json.RootElement.GetProperty("data").GetProperty("contractReady").GetBoolean(), Is.True);
        Assert.That(Directory.GetFileSystemEntries(_parent), Is.Empty);
    }
}
