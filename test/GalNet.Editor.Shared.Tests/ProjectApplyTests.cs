using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;

namespace GeneralTest.Editor;

public sealed class ProjectApplyTests
{
    private string _directory = null!;
    private string _project = null!;
    private string _staging = null!;
    private IEntryCatalog _catalog = null!;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "GalNet-apply-test-" + Guid.NewGuid().ToString("N"));
        _project = Path.Combine(_directory, "project"); _staging = Path.Combine(_directory, "staging"); Directory.CreateDirectory(_staging);
        await EditorProjectCreator.CreateAsync(new(_project, "Apply"));
        _catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
    }
    [TearDown] public void TearDown() => Directory.Delete(_directory, true);

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private ProjectApplyWrite Write(string path, string content)
    {
        var source = Guid.NewGuid().ToString("N"); File.WriteAllText(Path.Combine(_staging, source), content);
        return new() { Path = path, Source = source, ContentHash = Hash(Path.Combine(_staging, source)), ExpectedHash = File.Exists(Path.Combine(_project, path)) ? Hash(Path.Combine(_project, path)) : null };
    }
    private string Manifest(IReadOnlyList<ProjectApplyWrite> writes, IReadOnlyList<ProjectApplyDelete>? deletes = null)
    {
        var file = Path.Combine(_staging, "apply.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new ProjectApplyManifest { Version = 1, Writes = writes, Deletes = deletes ?? [] }, JsonOptions));
        return file;
    }
    private string GraphWithNewGroup()
    {
        var graph = JsonNode.Parse(File.ReadAllText(Path.Combine(_project, "Graph/graph.json")))!.AsObject();
        var opening = graph["nodes"]!.AsArray().Single(node => node!["type"]!.GetValue<string>() == "Group")!["id"]!.GetValue<string>();
        graph["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "generated", ["type"] = "Group", ["name"] = "Generated scene", ["x"] = 500, ["y"] = 0, ["file"] = "groups/generated.rawgalgroup" });
        graph["edges"]!.AsArray().Add(new JsonObject { ["id"] = "to-generated", ["fromNodeId"] = opening, ["fromOutlet"] = 0, ["toNodeId"] = "generated" });
        return graph.ToJsonString();
    }
    private ProjectApplyWrite[] Batch() =>
    [
        Write("Graph/graph.json", GraphWithNewGroup()),
        Write("Graph/groups/generated.rawgalgroup", "{\"version\":2,\"kind\":\"Raw\",\"entries\":[]}"),
        Write("Assets/actors/alice.png", "generated image bytes"),
        Write("Assets/actors/alice.png.meta", JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), type = "sprite" }))
    ];

    [Test]
    public async Task DryRunAndApplyValidateAndPublishGroupsAndResourcesTogether()
    {
        var original = File.ReadAllBytes(Path.Combine(_project, "Graph/graph.json"));
        var manifest = Manifest(Batch()); var service = new ProjectApplyService(_project, _catalog);
        var preview = await service.ApplyAsync(manifest, true);
        Assert.That(preview.Success, Is.True, JsonSerializer.Serialize(preview.Diagnostics));
        Assert.That(File.ReadAllBytes(Path.Combine(_project, "Graph/graph.json")), Is.EqualTo(original));
        Assert.That(File.Exists(Path.Combine(_project, "Assets/actors/alice.png")), Is.False);
        var result = await service.ApplyAsync(manifest);
        Assert.That(result.Success, Is.True, JsonSerializer.Serialize(result.Diagnostics));
        Assert.That(File.Exists(Path.Combine(_project, "Graph/groups/generated.rawgalgroup")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(_project, "Assets/actors/alice.png")), Is.EqualTo("generated image bytes"));
        Assert.That(((ProjectApplyReport)result.Data!).WrittenFiles, Has.Count.EqualTo(4));
        Assert.That(Directory.GetDirectories(Path.Combine(_project, ".galnet/editor/transactions")), Is.Empty);
    }

    [Test]
    public async Task StaleTargetAndChangedSourceRefuseTheWholeBatch()
    {
        var writes = Batch(); var manifest = Manifest(writes);
        File.AppendAllText(Path.Combine(_project, "Graph/graph.json"), " ");
        var result = await new ProjectApplyService(_project, _catalog).ApplyAsync(manifest);
        Assert.That(result.Success, Is.False); Assert.That(File.Exists(Path.Combine(_project, "Assets/actors/alice.png")), Is.False);
        writes = Batch(); manifest = Manifest(writes);
        File.AppendAllText(Path.Combine(_staging, writes[1].Source), "changed");
        result = await new ProjectApplyService(_project, _catalog).ApplyAsync(manifest);
        Assert.That(result.Success, Is.False); Assert.That(File.Exists(Path.Combine(_project, "Graph/groups/generated.rawgalgroup")), Is.False);
    }

    [Test]
    public async Task InvalidFinalProjectAndUnpairedResourceDoNotPublish()
    {
        var writes = Batch().ToList(); writes[1] = Write(writes[1].Path, "{");
        var result = await new ProjectApplyService(_project, _catalog).ApplyAsync(Manifest(writes));
        Assert.That(result.Success, Is.False); Assert.That(File.Exists(Path.Combine(_project, "Assets/actors/alice.png")), Is.False);
        result = await new ProjectApplyService(_project, _catalog).ApplyAsync(Manifest([Write("Assets/alone.png", "bytes")]));
        Assert.That(result.Success, Is.False); Assert.That(File.Exists(Path.Combine(_project, "Assets/alone.png")), Is.False);
    }

    [Test]
    public async Task PublicationFailureRestoresEveryEarlierFile()
    {
        var original = File.ReadAllBytes(Path.Combine(_project, "Graph/graph.json"));
        var result = await new ProjectApplyService(_project, _catalog, new FailSecondPublish()).ApplyAsync(Manifest(Batch()));
        Assert.That(result.Success, Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(_project, "Graph/graph.json")), Is.EqualTo(original));
        Assert.That(File.Exists(Path.Combine(_project, "Graph/groups/generated.rawgalgroup")), Is.False);
        Assert.That(File.Exists(Path.Combine(_project, "Assets/actors/alice.png")), Is.False);
    }

    [Test]
    public async Task ResourceDeletionRequiresRemainingJsonReferencesToBeUpdatedInTheSameBatch()
    {
        var service = new ProjectApplyService(_project, _catalog);
        Assert.That((await service.ApplyAsync(Manifest(Batch()))).Success, Is.True);
        var meta = "Assets/actors/alice.png.meta"; var image = "Assets/actors/alice.png";
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_project, meta)));
        Directory.CreateDirectory(Path.Combine(_project, "UI"));
        File.WriteAllText(Path.Combine(_project, "UI/ui.json"), JsonSerializer.Serialize(new { image = metadata.RootElement.GetProperty("id").GetString() }));
        ProjectApplyDelete[] deleted = [new() { Path = image, ExpectedHash = Hash(Path.Combine(_project, image)) }, new() { Path = meta, ExpectedHash = Hash(Path.Combine(_project, meta)) }];
        var rejected = await service.ApplyAsync(Manifest([], deleted)); Assert.That(rejected.Success, Is.False);
        Assert.That(rejected.Diagnostics.Any(diagnostic => diagnostic.Code == "project.applyReference"), Is.True);
        Assert.That((await service.ApplyAsync(Manifest([Write("UI/ui.json", "{}")], deleted))).Success, Is.True);
        Assert.That(File.Exists(Path.Combine(_project, image)), Is.False);
    }

    [TestCase("../escape.json")]
    [TestCase("Contracts/unsafe.dll")]
    [TestCase(".git/config")]
    [TestCase(".galnet/editor-state.json")]
    public async Task ProtectedAndEscapingTargetsAreRejected(string path)
    {
        var source = Write("notes.txt", "data");
        var write = new ProjectApplyWrite { Path = path, Source = source.Source, ContentHash = source.ContentHash, ExpectedHash = null };
        var result = await new ProjectApplyService(_project, _catalog).ApplyAsync(Manifest([write]));
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task CliDiscoversTheManifestContractAndRejectsMalformedInputWithJsonDiagnostics()
    {
        var help = await Shared.CliHelpTests.Invoke(["help", "project", "apply", "--json"]);
        Assert.That(help.ExitCode, Is.Zero, help.Error);
        using var document = JsonDocument.Parse(help.Output);
        Assert.That(document.RootElement.GetProperty("data").GetProperty("operations")[0].GetProperty("documentInputSchema").GetProperty("properties").TryGetProperty("writes", out _), Is.True);
        var manifest = Path.Combine(_staging, "bad.json"); File.WriteAllText(manifest, "{}");
        // The service catches malformed manifests before any mutation; a real CLI project supplies Contracts/.
        Assert.That((await new ProjectApplyService(_project, _catalog).ApplyAsync(manifest)).Success, Is.False);
    }

    [Test]
    public async Task RealCliAppliesTheSameReviewedManifestAndShipsItsAuthoringSkill()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var contracts = Path.Combine(root.FullName, "src/Editor/GalNet.Editor.DefaultContracts/bin", configuration, "net10.0");
        var cliProject = Path.Combine(_directory, "cli-project");
        await EditorProjectCreator.CreateAsync(new(cliProject, "CLI", contracts)); _project = cliProject;
        var writes = Batch().ToList();
        writes[1] = Write("Graph/groups/generated.rawgalgroup", """
            {"version":2,"kind":"Raw","entries":[
              {"id":"enter-wait","type":"flow.wait","parameters":{"duration":0.2}},
              {"id":"line-one","type":"dialogue.text","parameters":{"speaker":"alice","content":"scene.intro.line1"}}
            ]}
            """);
        writes.Add(Write("I18n/catalog/characters.json", """[{"id":"alice","displayNameKey":"character.alice.name"}]"""));
        writes.Add(Write("I18n/zh-CN/generated.json", """{"character.alice.name":"爱丽丝","scene.intro.line1":"我们开始吧。"}"""));
        var manifest = Manifest(writes);
        foreach (var arguments in new[] { new[] { "project", "apply", _project, manifest, "--dry-run" }, new[] { "project", "apply", _project, manifest } })
        {
            var invoked = await Shared.CliHelpTests.Invoke(arguments);
            Assert.That(invoked.ExitCode, Is.Zero, invoked.Output + invoked.Error);
            using var json = JsonDocument.Parse(invoked.Output);
            Assert.That(json.RootElement.GetProperty("operationId").GetString(), Is.EqualTo("project.apply"));
            Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.True);
        }
        Assert.That(File.Exists(Path.Combine(_project, "Assets/actors/alice.png")), Is.True);
        var compiled = await Shared.CliHelpTests.Invoke(["group", "compile", _project, "--id", "generated", "--dry-run"]);
        Assert.That(compiled.ExitCode, Is.Zero, compiled.Output + compiled.Error);
        Assert.That(File.Exists(Path.Combine(root.FullName, "src/Editor/GalNet.Editor.Headless/bin", configuration, "net10.0/Skills/galnet-project-authoring/SKILL.md")), Is.True);
    }

    private sealed class FailSecondPublish : AuthorFileTransaction
    {
        private int _count;
        protected override void Publish(string path, string? stagedFile)
        { if (++_count == 2) throw new IOException("Injected second-file publication failure."); base.Publish(path, stagedFile); }
    }
}
