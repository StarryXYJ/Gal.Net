using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;

namespace GeneralTest.Editor;

public class TargetProfileEditorCommandTests
{
    [Test]
    public void ConnectingAnotherPredecessorPreservesConvergenceAndReplacesOnlyItsOutput()
    {
        var handler = new BuiltInEditorCommandHandler(new TargetProfileEntryCatalog([]));
        var document = new EditorProjectDocument
        {
            Graph = new()
            {
                RootNodeId = "entry",
                Nodes =
        [new() { Id = "entry", Type = "Entry" }, new() { Id = "a", Type = "Group" }, new() { Id = "b", Type = "Group" }, new() { Id = "c", Type = "Group" }]
            }
        };
        Assert.That(handler.Execute(document, new ConnectNodesCommand("a", 0, "c", "a-c"), new(0, false)).Success, Is.True);
        Assert.That(handler.Execute(document, new ConnectNodesCommand("b", 0, "c", "b-c"), new(0, false)).Success, Is.True);
        Assert.That(document.Graph.Edges.Count(edge => edge.ToNodeId == "c"), Is.EqualTo(2));
        Assert.That(handler.Execute(document, new ConnectNodesCommand("a", 0, "b", "a-b"), new(0, false)).Success, Is.True);
        Assert.That(document.Graph.Edges.Select(edge => edge.Id), Is.EquivalentTo(new[] { "a-b", "b-c" }));
    }

    [Test]
    public void EntryCommandsOnlyAcceptTypesFromTheirInjectedProfile()
    {
        var catalog = new TargetProfileEntryCatalog(
        [new DefaultEntryModule("custom",
        [
            new DefaultPrimitiveEntryBase("custom.pulse", new DynamicParameterTable([new DynamicParameterDescriptor("count", typeof(int), isRequired: true)]))
        ])]);
        var handler = new BuiltInEditorCommandHandler(catalog);
        var document = new EditorProjectDocument
        {
            Graph = new EditorGraphDocument { Nodes = [new EditorGraphNodeDto { Id = "group", Type = "Group" }] },
            GroupEntries = { ["group"] = [] }
        };

        var added = handler.Execute(document, new AddEntryCommand("group", "pulse", Type: "custom.pulse", Parameters: new Dictionary<string, string> { ["count"] = "4" }), new EditorCommandContext(0, false));
        var rejected = handler.Execute(document, new SetEntryTypeCommand("group", "pulse", "dialogue.text"), new EditorCommandContext(0, false));

        Assert.That(added.Success, Is.True);
        Assert.That(document.GroupEntries["group"].Single().Type, Is.EqualTo("custom.pulse"));
        Assert.That(rejected.Success, Is.False);
        Assert.That(rejected.Diagnostics.Single().Code, Is.EqualTo("group.entry.unknownType"));
    }

    [Test]
    public void UnknownCommandParameters_AreRejectedBeforeMutation()
    {
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom",
            [new DefaultPrimitiveEntryBase("custom.pulse", new DynamicParameterTable([new DynamicParameterDescriptor("count", typeof(int))]))])]);
        var handler = new BuiltInEditorCommandHandler(catalog);
        var document = new EditorProjectDocument
        {
            Graph = new EditorGraphDocument { Nodes = [new EditorGraphNodeDto { Id = "group", Type = "Group" }] },
            GroupEntries = { ["group"] = [new EditorEntryData { StableId = "pulse", Type = "custom.pulse", Parameters = new() { ["count"] = System.Text.Json.JsonSerializer.SerializeToElement(4) } }] }
        };
        var operations = new IProjectEditCommand[]
        {
            new AddEntryCommand("group", "extra", Type: "custom.pulse", Parameters: new Dictionary<string, string> { ["unknown"] = "5" }),
            new SetEntryParametersCommand("group", "pulse", new Dictionary<string, string> { ["unknown"] = "5" }),
            new PatchEntryParametersCommand("group", "pulse", new Dictionary<string, string?> { ["unknown"] = null })
        };
        foreach (var command in operations)
        {
            var result = handler.Execute(document, command, new(0, false));
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("group.entry.unknownParameter"));
            Assert.That(document.GroupEntries["group"], Has.Count.EqualTo(1));
            Assert.That(document.GroupEntries["group"][0].Parameters["count"].GetInt32(), Is.EqualTo(4));
        }
    }
}
