using System.IO;
using System.Linq;
using System.Text.Json;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

[TestFixture]
public class EditorDocumentRepositoryTests
{
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
    public void Load_MigratesVariablesFromProjectSettings_WhenGraphDocumentDoesNotContainThem()
    {
        var groupId = "group_1";
        var document = new EditorGraphDocument
        {
            Name = "Demo",
            RootNodeId = "entry_1",
            Nodes =
            [
                new EditorGraphNodeDto { Id = "entry_1", Type = "Entry", Name = "Entry", X = 0, Y = 0 },
                new EditorGraphNodeDto { Id = groupId, Type = "Group", Name = "Start", X = 10, Y = 20, File = $"groups/{groupId}.galgroup" }
            ],
            Edges = [new EditorGraphEdgeDto { FromNodeId = "entry_1", FromOutlet = 0, ToNodeId = groupId }]
        };

        File.WriteAllText(
            Path.Combine(_tempDir, "Graph", "graph.json"),
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(_tempDir, "Graph", "groups", $"{groupId}.galgroup"),
            GalNet.Core.Serialization.GalgroupParser.Serialize("text", new() { ["text"] = "hello" }));

        var settings = new ProjectSettings
        {
            PlayerVariables =
            [
                new() { Name = "player_flag", DefaultValue = new GalNet.Core.Variable.Variable { Name = "player_flag", Value = VariableValue.From(false) } }
            ],
            SaveVariables =
            [
                new() { Name = "save_count", DefaultValue = new GalNet.Core.Variable.Variable { Name = "save_count", Value = VariableValue.From(1) } }
            ]
        };

        var repository = new EditorDocumentRepository();
        var loaded = repository.Load(_tempDir, "Demo", settings);

        Assert.That(loaded.Document.PlayerVariables.Select(v => v.Name), Is.EqualTo(new[] { "player_flag" }));
        Assert.That(loaded.Document.SaveVariables.Select(v => v.Name), Is.EqualTo(new[] { "save_count" }));
        Assert.That(loaded.GroupEntries[groupId], Has.Count.EqualTo(1));
        Assert.That(loaded.GroupEntries[groupId][0].Type, Is.EqualTo("text"));
    }

    [Test]
    public void Save_WritesVariablesAndGroupEntriesIntoProjectFiles()
    {
        var repository = new EditorDocumentRepository();
        var document = new EditorGraphDocument
        {
            Name = "Demo",
            RootNodeId = "entry_1",
            Nodes =
            [
                new EditorGraphNodeDto { Id = "entry_1", Type = "Entry", Name = "Entry", X = 0, Y = 0 },
                new EditorGraphNodeDto { Id = "group_1", Type = "Group", Name = "Start", X = 10, Y = 20, File = "groups/group_1.galgroup" }
            ],
            Edges = [new EditorGraphEdgeDto { FromNodeId = "entry_1", FromOutlet = 0, ToNodeId = "group_1" }],
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
                        Type = "text",
                        Condition = "player_name==Alice",
                        Parameters = "speaker=Alice; text=Hello"
                    }
                ]
            });

        var savedJson = File.ReadAllText(Path.Combine(_tempDir, "Graph", "graph.json"));
        var savedDocument = JsonSerializer.Deserialize<EditorGraphDocument>(savedJson);
        var savedGroup = File.ReadAllText(Path.Combine(_tempDir, "Graph", "groups", "group_1.galgroup"));

        Assert.That(savedDocument, Is.Not.Null);
        Assert.That(savedDocument!.PlayerVariables.Select(v => v.Name), Is.EqualTo(new[] { "player_name" }));
        Assert.That(savedDocument.SaveVariables.Select(v => v.Name), Is.EqualTo(new[] { "save_slot" }));
        Assert.That(savedGroup, Does.Contain("condition: player_name==Alice"));
        Assert.That(savedGroup, Does.Contain("speaker: Alice"));
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
