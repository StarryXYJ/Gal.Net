using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;

namespace GeneralTest.Editor;

public class TargetProfileEditorCommandTests
{
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
            GroupEntries = { ["group"] = [new EditorEntryData { StableId = "pulse", Type = "custom.pulse", Parameters = new() { ["count"] = "4" } }] }
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
            Assert.That(document.GroupEntries["group"][0].Parameters["count"], Is.EqualTo("4"));
        }
    }
}
