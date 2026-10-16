using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;

namespace GeneralTest.Editor;

public class EntryIdentityScopeTests
{
    [Test]
    public void AddingEntriesAllowsTheSameIdInDifferentGroupsButRejectsItInTheSameGroup()
    {
        var document = CreateDocument();
        var handler = new BuiltInEditorCommandHandler(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
        var context = new EditorCommandContext(0, false);

        Assert.That(handler.Execute(document, new AddEntryCommand("first", "show", Type: "dialogue.show"), context).Success, Is.True);
        Assert.That(handler.Execute(document, new AddEntryCommand("second", "show", Type: "dialogue.show"), context).Success, Is.True);
        var duplicate = handler.Execute(document, new AddEntryCommand("first", "show", Type: "dialogue.show"), context);

        Assert.That(duplicate.Success, Is.False);
        Assert.That(duplicate.Diagnostics.Single().Code, Is.EqualTo("group.entry.duplicateId"));
        Assert.That(document.GroupEntries["first"], Has.Count.EqualTo(1));
    }

    [Test]
    public void ValidationChecksEntryIdsWithinEachGroup()
    {
        var document = CreateDocument();
        document.GroupEntries["first"].Add(new EditorEntryData { StableId = "line" });
        document.GroupEntries["second"].Add(new EditorEntryData { StableId = "line" });
        Assert.That(EditorDocumentValidator.Validate(document).IsValid, Is.True);

        document.GroupEntries["second"].Add(new EditorEntryData { StableId = "line" });
        var result = EditorDocumentValidator.Validate(document);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("group.entry.duplicateId"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain("second"));
    }

    private static EditorProjectDocument CreateDocument() => new()
    {
        Graph = new EditorGraphDocument
        {
            RootNodeId = "first",
            Nodes = [new EditorGraphNodeDto { Id = "first", Type = "Group" }, new EditorGraphNodeDto { Id = "second", Type = "Group" }]
        },
        GroupEntries = { ["first"] = [], ["second"] = [] }
    };
}
