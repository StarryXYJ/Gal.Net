using System.Text;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GeneralTest.Editor.Shared;

namespace GalNet.Editor.Shared.Tests;

public sealed class ProjectAuthoringSafetyTests
{
    private string _root = null!;
    private IEntryCatalog _catalog = null!;
    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-author-" + Guid.NewGuid())).FullName;
        _catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        Put("Graph/graph.json", """{"version":2,"name":"Original","rootNodeId":"entry","nodes":[{"id":"entry","name":"Entry","type":"Entry"},{"id":"a","name":"A","type":"Group","file":"groups/a.rawgalgroup"}],"edges":[{"id":"edge","fromNodeId":"entry","fromOutlet":0,"toNodeId":"a"}],"playerVariables":[],"saveVariables":[]}""");
        Put("Graph/groups/a.rawgalgroup", """{"version":2,"kind":"Raw","entries":[{"id":"text","type":"dialogue.text","parameters":{"content":""}}]}""");
        Put("settings.json", "{}");
    }
    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [TestCase("Graph/graph.json", "\"version\":2", "\"version\":1")]
    [TestCase("Graph/graph.json", "\"name\":\"Original\"", "\"name\":\"Original\",\"unknown\":123")]
    [TestCase("Graph/graph.json", "\"id\":\"edge\",", "")]
    [TestCase("Graph/groups/a.rawgalgroup", "\"id\":\"text\",", "")]
    [TestCase("Graph/groups/a.rawgalgroup", "\"content\":\"\"", "\"content\":\"\",\"unknown\":true")]
    public void Load_RejectsOldInvalidOrUnknownDataWithoutChangingFiles(string file, string before, string after)
    {
        var path = Path.Combine(_root, file);
        File.WriteAllText(path, File.ReadAllText(path).Replace(before, after, StringComparison.Ordinal));
        var original = Snapshot();
        Assert.That(() => Persistence().Load(), Throws.TypeOf<InvalidDataException>());
        AssertSnapshot(original);
    }

    [Test]
    public void UnknownSettings_AreRejectedWithoutLoss()
    {
        Put("settings.json", """{"custom":{"keep":true}}""");
        var original = Snapshot();
        Assert.That(() => Persistence().Load(), Throws.TypeOf<JsonException>().With.Message.Contains("custom"));
        AssertSnapshot(original);
    }

    [Test]
    public void DuplicateSettings_AreRejectedWithoutLoss()
    {
        Put("settings.json", """{"defaultWidth":1920,"defaultWidth":800}""");
        var original = Snapshot();
        Assert.That(() => Persistence().Load(), Throws.TypeOf<InvalidDataException>().With.Message.Contains("Duplicate"));
        AssertSnapshot(original);
    }

    [Test]
    public void SaveWithoutPriorLoad_StillRejectsUnknownSourceData()
    {
        Put("Graph/groups/a.rawgalgroup", """{"version":2,"kind":"Raw","entries":[],"custom":true}""");
        var original = Snapshot();
        var graph = JsonSerializer.Deserialize<EditorGraphDocument>(File.ReadAllText(Path.Combine(_root, "Graph/graph.json")))!;
        Assert.That(() => new EditorDocumentRepository(_catalog).Save(_root, graph,
            new Dictionary<string, IReadOnlyList<EditorEntryData>> { ["a"] = [] }), Throws.TypeOf<InvalidDataException>());
        AssertSnapshot(original);
    }

    [Test]
    public async Task Save_PreservesEmptyStringAndStableIds_AndDeletesGroupSourceWithNode()
    {
        var persistence = Persistence();
        var document = persistence.Load();
        document.Graph.Name = "Renamed";
        await persistence.SaveAsync(document);
        using var group = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_root, "Graph/groups/a.rawgalgroup")));
        Assert.That(group.RootElement.GetProperty("entries")[0].GetProperty("parameters").GetProperty("content").GetString(), Is.EqualTo(""));
        Assert.That(group.RootElement.GetProperty("entries")[0].GetProperty("id").GetString(), Is.EqualTo("text"));
        var result = new BuiltInEditorCommandHandler(_catalog).Execute(document, new DeleteNodeCommand("a"), new(0, false));
        Assert.That(result.Success, Is.True);
        await persistence.SaveAsync(document);
        Assert.That(File.Exists(Path.Combine(_root, "Graph/groups/a.rawgalgroup")), Is.False);
        Assert.That(new ProjectValidationService(_root).Validate("graph").Success, Is.True);
    }

    [Test]
    public void PrepareSave_IsReadOnly_AndRejectsInvalidNewParameters()
    {
        var persistence = Persistence();
        var document = persistence.Load();
        var original = Snapshot();
        document.Graph.Name = "Preview";
        Assert.That(persistence.PrepareSave(document).Writes, Contains.Key("Graph/graph.json"));
        AssertSnapshot(original);
        document.GroupEntries["a"][0].Parameters["unknown"] = "value";
        Assert.That(() => persistence.PrepareSave(document), Throws.TypeOf<InvalidDataException>());
        AssertSnapshot(original);
    }

    [Test]
    public async Task ExternalSourceChange_RejectsStaleSave()
    {
        var persistence = Persistence();
        var document = persistence.Load();
        Put("Graph/groups/a.rawgalgroup", """{"version":2,"kind":"Raw","entries":[]}""");
        var original = Snapshot();
        document.Graph.Name = "Stale";
        Assert.ThrowsAsync<IOException>(() => persistence.SaveAsync(document));
        AssertSnapshot(original);
        await Task.CompletedTask;
    }

    [Test]
    public void LaterPublicationFailure_RestoresEarlierGroupAndGraph_AndCleansStages()
    {
        var original = Snapshot();
        using (var locked = new FileStream(Path.Combine(_root, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.That(() => ProjectAuthorFileTransaction.Commit(_root, new Dictionary<string, byte[]>
            {
                ["Graph/groups/a.rawgalgroup"] = Encoding.UTF8.GetBytes("changed group"),
                ["Graph/graph.json"] = Encoding.UTF8.GetBytes("changed graph"),
                ["settings.json"] = Encoding.UTF8.GetBytes("changed settings")
            }, []), Throws.Exception.Matches<Exception>(exception => exception is IOException or UnauthorizedAccessException));
        }
        AssertSnapshot(original);
        Assert.That(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void Cancellation_DoesNotPublishAnyFile()
    {
        var original = Snapshot();
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectAuthorFileTransaction.Commit(_root,
            new Dictionary<string, byte[]> { ["Graph/graph.json"] = Encoding.UTF8.GetBytes("changed") }, [], source.Token));
        AssertSnapshot(original);
        Assert.That(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void RepositoryTracksOnlyItsOwnFiles_WhenEditorSavesSettingsSeparately()
    {
        var repository = new EditorDocumentRepository(_catalog);
        var loaded = repository.Load(_root, "Original", new());
        var entries = loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value);
        loaded.Document.Name = "First";
        repository.Save(_root, loaded.Document, entries);
        Put("settings.json", """{"defaultWidth":800}""");
        loaded.Document.Name = "Second";
        repository.Save(_root, loaded.Document, entries);
        Assert.That(File.ReadAllText(Path.Combine(_root, "settings.json")), Does.Contain("800"));
        Assert.That(File.ReadAllText(Path.Combine(_root, "Graph/graph.json")), Does.Contain("Second"));
    }

    [Test]
    public void DirectPersistence_RejectsConcurrentSettingsChange()
    {
        var persistence = Persistence();
        var document = persistence.Load();
        Put("settings.json", """{"defaultWidth":800}""");
        var original = Snapshot();
        Assert.That(() => persistence.PrepareSave(document), Throws.TypeOf<IOException>());
        AssertSnapshot(original);
    }

    [TestCase("Graph/../Output/example.rawgalgroup")]
    [TestCase("Graph/groups/../groups/a.rawgalgroup")]
    public void TransactionRejectsNonCanonicalPaths_BeforeAnyWrite(string file)
    {
        var original = Snapshot();
        Assert.Throws<ArgumentException>(() => ProjectAuthorFileTransaction.Commit(_root,
            new Dictionary<string, byte[]> { [file] = Encoding.UTF8.GetBytes("changed") }, []));
        AssertSnapshot(original);
    }

    [Test]
    public void Preview_PreservesAuthorIdentityAndDoesNotDiscardUnknownParameters()
    {
        var repository = new EditorDocumentRepository(_catalog);
        var loaded = repository.Load(_root, "Original", new());
        var entries = loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value);
        var coordinator = new EditorSaveCoordinator(repository, _catalog);
        var preview = Path.Combine(_root, "Temp/Preview");
        coordinator.BuildPreviewData(preview, loaded.Document, entries);
        using var graph = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(preview, "graph.json")));
        Assert.That(graph.RootElement.GetProperty("edges")[0].GetProperty("id").GetString(), Is.EqualTo("edge"));
        var source = graph.RootElement.GetProperty("nodes")[1].GetProperty("file").GetString()!;
        Assert.That(File.Exists(Path.Combine(preview, source)), Is.True);
        loaded.GroupEntries["a"][0].Parameters["unknown"] = "value";
        Assert.That(() => coordinator.BuildPreviewData(preview, loaded.Document, entries), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Preview_GroupIdsCannotBecomeOutputPaths()
    {
        var repository = new EditorDocumentRepository(_catalog);
        var loaded = repository.Load(_root, "Original", new());
        loaded.Document.Nodes[1].Id = "../../escape";
        loaded.Document.Edges[0].ToNodeId = "../../escape";
        var entries = new Dictionary<string, IReadOnlyList<EditorEntryData>> { ["../../escape"] = loaded.GroupEntries["a"] };
        var preview = Path.Combine(_root, "Temp/Preview");
        new EditorSaveCoordinator(repository, _catalog).BuildPreviewData(preview, loaded.Document, entries);
        using var graph = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(preview, "graph.json")));
        var file = graph.RootElement.GetProperty("nodes")[1].GetProperty("file").GetString()!;
        Assert.That(file, Is.EqualTo(Path.GetFileName(file)));
        Assert.That(File.Exists(Path.Combine(preview, file)), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, "escape.galgroup")), Is.False);
        Assert.That(graph.RootElement.GetProperty("nodes")[1].GetProperty("id").GetString(), Is.EqualTo("../../escape"));
    }

    [Test]
    public void Preview_RejectsLinkedOutputBeforeWriting()
    {
        var repository = new EditorDocumentRepository(_catalog);
        var loaded = repository.Load(_root, "Original", new());
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var link = Path.Combine(_root, "preview-link");
        Directory.CreateSymbolicLink(link, target);
        try
        {
            var entries = loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value);
            Assert.Throws<InvalidDataException>(() => new EditorSaveCoordinator(repository, _catalog).BuildPreviewData(link, loaded.Document, entries));
            Assert.That(Directory.GetFiles(target), Is.Empty);
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public async Task RealCli_DryRunUsesTheSameStrictSaveValidation()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        Directory.CreateDirectory(Path.Combine(_root, "Contracts"));
        foreach (var file in Directory.GetFiles(Path.Combine(directory.FullName, "test/Fixtures/SchemaA/bin", configuration, "net10.0")))
            File.Copy(file, Path.Combine(_root, "Contracts", Path.GetFileName(file)));
        var command = Path.Combine(_root, "command.json");
        File.WriteAllText(command, """{"commandId":"project.rename","name":"DryRun","dryRun":true}""");
        var original = Snapshot();
        var result = await CliHelpTests.Invoke(["execute", _root, command]);
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        AssertSnapshot(original);
    }

    private DirectProjectPersistence Persistence() => new(_root, new EditorDocumentRepository(_catalog));
    private void Put(string file, string content)
    {
        var path = Path.Combine(_root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
    private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
    private void AssertSnapshot(Dictionary<string, byte[]> expected)
    {
        var actual = Snapshot();
        Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
        foreach (var (path, bytes) in expected) Assert.That(actual[path], Is.EqualTo(bytes), path);
    }
}
