using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

[TestFixture]
public class EditorDocumentRepositoryTests
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private static readonly string[] ExpectedPlayerName = ["player_name"];
    private static readonly string[] ExpectedSaveSlot = ["save_slot"];
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GalNet.Editor.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "Graph", "groups"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Test]
    public void Load_UsesGraphVariables_AndRejectsUnknownParameters()
    {
        var groupId = "group_1";
        var document = new EditorGraphDocument
        {
            Name = "Demo",
            RootNodeId = "entry_1",
            Nodes =
            [
                new EditorGraphNodeDto { Id = "entry_1", Type = "Entry", Name = "Entry", X = 0, Y = 0 },
                new EditorGraphNodeDto { Id = groupId, Type = "Group", Name = "Start", X = 10, Y = 20, File = $"groups/{groupId}.rawgalgroup" }
            ],
            Edges = [new EditorGraphEdgeDto { Id = "edge-1", FromNodeId = "entry_1", FromOutlet = 0, ToNodeId = groupId }]
        };

        File.WriteAllText(
            Path.Combine(_tempDir, "Graph", "graph.json"),
            JsonSerializer.Serialize(document, IndentedJsonOptions));
        File.WriteAllText(
            Path.Combine(_tempDir, "Graph", "groups", $"{groupId}.rawgalgroup"),
            """{ "version": 2, "kind": "Raw", "entries": [ { "id": "entry-1", "type": "dialogue.text", "parameters": { "content": "hello", "obsolete": "discard" } } ] }""");

        var repository = new EditorDocumentRepository(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
        Assert.That(() => repository.Load(_tempDir), Throws.TypeOf<InvalidDataException>().With.Message.Contains("obsolete"));
        File.WriteAllText(Path.Combine(_tempDir, "Graph", "groups", $"{groupId}.rawgalgroup"),
            """{"version":2,"kind":"Raw","entries":[{"id":"entry-1","type":"dialogue.text","parameters":{"content":"hello"}}]}""");
        var loaded = repository.Load(_tempDir);

        Assert.That(loaded.Document.PlayerVariables, Is.Empty);
        Assert.That(loaded.Document.SaveVariables, Is.Empty);
        Assert.That(loaded.GroupEntries[groupId], Has.Count.EqualTo(1));
        Assert.That(loaded.GroupEntries[groupId][0].Type, Is.EqualTo("dialogue.text"));
        Assert.That(loaded.GroupEntries[groupId][0].Parameters["content"].GetString(), Is.EqualTo("hello"));
        Assert.That(loaded.GroupEntries[groupId][0].Parameters, Does.Not.ContainKey("obsolete"));
    }

    [Test]
    public void Save_WritesVariablesAndGroupEntriesIntoProjectFiles()
    {
        var repository = new EditorDocumentRepository(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
        var document = new EditorGraphDocument
        {
            Name = "Demo",
            RootNodeId = "entry_1",
            Nodes =
            [
                new EditorGraphNodeDto { Id = "entry_1", Type = "Entry", Name = "Entry", X = 0, Y = 0 },
                new EditorGraphNodeDto { Id = "group_1", Type = "Group", Name = "Start", X = 10, Y = 20, File = "groups/group_1.rawgalgroup" }
            ],
            Edges = [new EditorGraphEdgeDto { Id = "edge-1", FromNodeId = "entry_1", FromOutlet = 0, ToNodeId = "group_1" }],
            PlayerVariables = [CreateDefinition("player_name", "Alice")],
            SaveVariables = [CreateDefinition("save_slot", 3)]
        };
        repository.Save(
            _tempDir,
            document,
            new Dictionary<string, IReadOnlyList<EditorEntryData>>
            {
                ["group_1"] =
                [
                    new EditorEntryData
                    {
                        Id = 1,
                        StableId = "text-1",
                        Type = "dialogue.text",
                        Condition = "[player_name] == 'Alice'",
                        Parameters = new() { ["speaker"] = JsonSerializer.SerializeToElement("Alice"), ["content"] = JsonSerializer.SerializeToElement("Hello") }
                    }
                ]
            });

        var savedJson = File.ReadAllText(Path.Combine(_tempDir, "Graph", "graph.json"));
        var savedDocument = JsonSerializer.Deserialize<EditorGraphDocument>(savedJson);
        var savedGroup = File.ReadAllText(Path.Combine(_tempDir, "Graph", "groups", "group_1.rawgalgroup"));

        Assert.That(savedDocument, Is.Not.Null);
        Assert.That(savedDocument!.PlayerVariables.Select(v => v.Name), Is.EqualTo(ExpectedPlayerName));
        Assert.That(savedDocument.SaveVariables.Select(v => v.Name), Is.EqualTo(ExpectedSaveSlot));
        var reloaded = repository.Load(_tempDir);
        var savedGroupDocument = JsonDocument.Parse(savedGroup);
        Assert.That(savedGroupDocument.RootElement.GetProperty("kind").GetString(), Is.EqualTo("Raw"));
        var entry = savedGroupDocument.RootElement.GetProperty("entries")[0];
        Assert.That(entry.GetProperty("condition").GetString(), Is.EqualTo("[player_name] == 'Alice'"));
        Assert.That(entry.GetProperty("parameters").GetProperty("speaker").GetString(), Is.EqualTo("Alice"));
        Assert.That(entry.GetProperty("parameters").GetProperty("content").GetString(), Is.EqualTo("Hello"));
    }

    [Test]
    public void RepositoryUsesOnlyTheInjectedTargetProfile()
    {
        const string groupId = "group_1";
        var catalog = new TargetProfileEntryCatalog(
        [new DefaultEntryModule("custom",
        [
            new DefaultPrimitiveEntryBase("custom.pulse", new DynamicParameterTable([new DynamicParameterDescriptor("count", typeof(int), isRequired: true)]))
        ])]);
        var graph = new EditorGraphDocument
        {
            Name = "Custom",
            RootNodeId = groupId,
            Nodes = [new EditorGraphNodeDto { Id = groupId, Name = "Custom", Type = "Group", File = $"groups/{groupId}.rawgalgroup" }]
        };
        File.WriteAllText(Path.Combine(_tempDir, "Graph", "graph.json"), JsonSerializer.Serialize(graph));
        File.WriteAllText(
            Path.Combine(_tempDir, "Graph", "groups", $"{groupId}.rawgalgroup"),
            """{ "version": 2, "kind": "Raw", "entries": [ { "id": "pulse-1", "type": "custom.pulse", "parameters": { "count": 3 } } ] }""");

        var repository = new EditorDocumentRepository(catalog);
        var loaded = repository.Load(_tempDir);
        repository.Save(_tempDir, loaded.Document, loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));

        Assert.That(loaded.GroupEntries[groupId].Single().Parameters["count"].GetInt32(), Is.EqualTo(3));
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, "Graph", "groups", $"{groupId}.rawgalgroup")));
        Assert.That(saved.RootElement.GetProperty("entries")[0].GetProperty("parameters").GetProperty("count").GetInt32(), Is.EqualTo(3));
    }

    [Test]
    public void RepositoryRoundTripsStringEncodedEnumUsingProjectJsonOptions()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom",
            [new DefaultPrimitiveEntryBase("custom.day", new DynamicParameterTable(
                [new DynamicParameterDescriptor("day", new ValueDescriptor(typeof(DayOfWeek), jsonOptions: options))]))])]);
        const string groupId = "days";
        var graph = new EditorGraphDocument
        {
            Name = "Days",
            RootNodeId = groupId,
            Nodes = [new EditorGraphNodeDto { Id = groupId, Name = "Days", Type = "Group", File = "groups/days.rawgalgroup" }]
        };
        File.WriteAllText(Path.Combine(_tempDir, "Graph", "graph.json"), JsonSerializer.Serialize(graph));
        var sourcePath = Path.Combine(_tempDir, "Graph", "groups", "days.rawgalgroup");
        File.WriteAllText(sourcePath,
            """{"version":2,"kind":"Raw","entries":[{"id":"day-1","type":"custom.day","parameters":{"day":"Monday"}}]}""");
        var repository = new EditorDocumentRepository(catalog);
        var loaded = repository.Load(_tempDir);
        Assert.That(loaded.GroupEntries[groupId][0].Parameters["day"].GetString(), Is.EqualTo("Monday"));
        loaded.GroupEntries[groupId][0].Parameters["day"] = JsonSerializer.SerializeToElement("Tuesday");
        repository.Save(_tempDir, loaded.Document,
            loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
        using var saved = JsonDocument.Parse(File.ReadAllText(sourcePath));
        Assert.That(saved.RootElement.GetProperty("entries")[0].GetProperty("parameters").GetProperty("day").GetString(), Is.EqualTo("Tuesday"));
    }

    private static ProjectVariableDefinition CreateDefinition(string name, object value)
    {
        var variable = new GalNet.Core.Variable.Variable { Name = name };
        variable.SetValue(value);
        return new ProjectVariableDefinition
        {
            Name = name,
            DefaultValue = variable
        };
    }
}
