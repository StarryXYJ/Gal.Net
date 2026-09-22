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
        [new PrimitiveDescriptor("custom.pulse", new DynamicParameterTable([new DynamicParameterDescriptor("count", typeof(int), isRequired: true)]))]);
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
}
