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
            new DefaultPrimitiveEntryBase(
                "custom.pulse",
                new DynamicParameterTable([new DynamicParameterDescriptor("count", typeof(int), isRequired: true)]),
                static _ => new ImmediatePrimitiveInstance())
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
    public void GalleryCommandsRegisterTypesAndPreserveStableItemIdsOnUpdate()
    {
        var handler = new BuiltInEditorCommandHandler(new TargetProfileEntryCatalog([]));
        var document = new EditorProjectDocument { Graph = new EditorGraphDocument() };

        var registered = handler.Execute(
            document,
            new RegisterGalleryTypeCommand("wallpaper", "sprite"),
            new EditorCommandContext(0, false));
        var added = handler.Execute(
            document,
            new SetGalleryItemCommand("stable_item", "wallpaper", "asset-1", "First", 2),
            new EditorCommandContext(1, false));
        var updated = handler.Execute(
            document,
            new SetGalleryItemCommand("stable_item", "wallpaper", "asset-1", "Updated", 1),
            new EditorCommandContext(2, false));

        Assert.Multiple(() =>
        {
            Assert.That(registered.Success, Is.True);
            Assert.That(added.Success, Is.True);
            Assert.That(updated.Success, Is.True);
            Assert.That(document.Graph.Gallery.Items, Has.Count.EqualTo(1));
            Assert.That(document.Graph.Gallery.Items.Single().Id, Is.EqualTo("stable_item"));
            Assert.That(document.Graph.Gallery.Items.Single().Title, Is.EqualTo("Updated"));
            Assert.That(document.Graph.Gallery.Items.Single().SortOrder, Is.EqualTo(1));
        });
    }
}
