using System.Text.Json;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Sessions;
using GalNet.Editor.Shared.Commands;

namespace GeneralTest.Editor;

[TestFixture]
public sealed class EditorSessionTests
{
    [Test]
    public void Transaction_IsAtomicAndUndoRedoRestoresDocument()
    {
        var session = CreateSession();

        var result = session.ExecuteTransaction(
        [
            new CreateNodeCommand("choice", EditorNodeKind.ChoiceBranch, "Choice", 300, 200),
            new AddChoiceOptionCommand("choice", "option_a", Text: "Continue"),
            new ConnectNodesCommand("group", 0, "choice", "edge_group_choice")
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(session.Revision, Is.EqualTo(1));
            Assert.That(session.Document.Graph.Nodes.Any(node => node.Id == "choice"), Is.True);
            Assert.That(session.Document.Graph.Edges.Any(edge => edge.Id == "edge_group_choice"), Is.True);
            Assert.That(session.CanUndo, Is.True);
        });

        var undo = session.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(undo.Success, Is.True);
            Assert.That(session.Document.Graph.Nodes.Any(node => node.Id == "choice"), Is.False);
            Assert.That(session.Document.Graph.Edges.Any(edge => edge.Id == "edge_group_choice"), Is.False);
            Assert.That(session.CanRedo, Is.True);
        });

        session.Redo();
        Assert.That(session.Document.Graph.Nodes.Any(node => node.Id == "choice"), Is.True);
    }

    [Test]
    public void FailedTransaction_DoesNotApplyEarlierCommands()
    {
        var session = CreateSession();

        var result = session.ExecuteTransaction(
        [
            new RenameNodeCommand("group", "Changed"),
            new DeleteNodeCommand("missing")
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(session.Revision, Is.Zero);
            Assert.That(session.Document.Graph.Nodes.Single(node => node.Id == "group").Name, Is.EqualTo("Opening"));
            Assert.That(session.CanUndo, Is.False);
        });
    }

    [Test]
    public void ChoiceOptionMove_PreservesAssociatedEdges()
    {
        var document = CreateDocument();
        document.Graph.Nodes.Add(new EditorGraphNodeDto
        {
            Id = "choice",
            Type = "Branch",
            BranchType = "Choice",
            Name = "Choice",
            Options =
            [
                new EditorGraphBranchOptionDto { Id = "a", Text = "A" },
                new EditorGraphBranchOptionDto { Id = "b", Text = "B" }
            ]
        });
        document.Graph.Nodes.Add(new EditorGraphNodeDto { Id = "target_a", Type = "Group", Name = "A" });
        document.Graph.Nodes.Add(new EditorGraphNodeDto { Id = "target_b", Type = "Group", Name = "B" });
        document.GroupEntries["target_a"] = [];
        document.GroupEntries["target_b"] = [];
        document.Graph.Edges.Add(new EditorGraphEdgeDto { Id = "edge_a", FromNodeId = "choice", FromOutlet = 0, ToNodeId = "target_a" });
        document.Graph.Edges.Add(new EditorGraphEdgeDto { Id = "edge_b", FromNodeId = "choice", FromOutlet = 1, ToNodeId = "target_b" });
        var session = new EditorSession(document, new MemoryPersistence());

        var result = session.Execute(new MoveChoiceOptionCommand("choice", "a", 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(session.Document.Graph.Nodes.Single(node => node.Id == "choice").Options!.Select(option => option.Id), Is.EqualTo(new[] { "b", "a" }));
            Assert.That(session.Document.Graph.Edges.Single(edge => edge.Id == "edge_a").FromOutlet, Is.EqualTo(1));
            Assert.That(session.Document.Graph.Edges.Single(edge => edge.Id == "edge_b").FromOutlet, Is.Zero);
        });
    }

    [Test]
    public void DryRunAndRevisionConflict_DoNotModifyDocument()
    {
        var session = CreateSession();

        var dryRun = session.Execute(
            new RenameNodeCommand("group", "Dry Run"),
            new ExecuteOptions(ExpectedRevision: 0, DryRun: true));
        var conflict = session.Execute(
            new RenameNodeCommand("group", "Conflict"),
            new ExecuteOptions(ExpectedRevision: 9));

        Assert.Multiple(() =>
        {
            Assert.That(dryRun.Success, Is.True);
            Assert.That(conflict.Success, Is.False);
            Assert.That(conflict.Diagnostics.Single().Code, Is.EqualTo("session.revisionConflict"));
            Assert.That(session.Revision, Is.Zero);
            Assert.That(session.Document.Graph.Nodes.Single(node => node.Id == "group").Name, Is.EqualTo("Opening"));
        });
    }

    [Test]
    public async Task SaveCheckpoint_TracksDirtyAcrossUndoAndRedo()
    {
        var persistence = new MemoryPersistence();
        var session = new EditorSession(CreateDocument(), persistence);
        session.Execute(new RenameNodeCommand("group", "Saved Name"));
        await session.SaveAsync();

        Assert.That(session.IsDirty, Is.False);
        session.Execute(new RenameNodeCommand("group", "Later Name"));
        Assert.That(session.IsDirty, Is.True);
        session.Undo();
        Assert.That(session.IsDirty, Is.False);
        session.Undo();
        Assert.That(session.IsDirty, Is.True);
        session.Redo();
        Assert.That(session.IsDirty, Is.False);
        Assert.That(persistence.SavedDocument, Is.Not.Null);
    }

    [Test]
    public void CommandsWithSameMergeKey_UndoAsSingleEdit()
    {
        var session = CreateSession();
        var options = new ExecuteOptions(MergeKey: "node:group:name", MergeWindow: TimeSpan.FromSeconds(5));

        session.Execute(new RenameNodeCommand("group", "O"), options);
        session.Execute(new RenameNodeCommand("group", "Op"), options);
        session.Execute(new RenameNodeCommand("group", "Open"), options);

        Assert.That(session.Document.Graph.Nodes.Single(node => node.Id == "group").Name, Is.EqualTo("Open"));
        session.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(session.Document.Graph.Nodes.Single(node => node.Id == "group").Name, Is.EqualTo("Opening"));
            Assert.That(session.CanUndo, Is.False);
        });
    }

    [Test]
    public void Catalog_DeserializesAgentCommandByStableId()
    {
        var catalog = new EditorCommandCatalog();
        using var json = JsonDocument.Parse("""
            { "commandId": "graph.node.rename", "nodeId": "group", "name": "Agent Name" }
            """);

        var command = catalog.Deserialize("graph.node.rename", json.RootElement);

        Assert.That(command, Is.EqualTo(new RenameNodeCommand("group", "Agent Name")));
        Assert.That(catalog.Find("graph.node.rename")!.Description, Does.StartWith("Changes"));
    }

    [Test]
    public void UiPaletteCommand_IsUndoableWithTheProjectDocument()
    {
        var session = CreateSession();
        var original = session.Document.UiProject.ColorPaletteId;

        var result = session.Execute(new ApplyUiColorPaletteCommand("rose-dusk"));

        Assert.That(result.Success, Is.True);
        Assert.That(session.Document.UiProject.ColorPaletteId, Is.EqualTo("rose-dusk"));
        session.Undo();
        Assert.That(session.Document.UiProject.ColorPaletteId, Is.EqualTo(original));
    }

    [Test]
    public void VariableCommand_PreservesCallerStableUidAcrossHistorySnapshots()
    {
        var session = CreateSession();
        var result = session.Execute(new AddVariableDefinitionCommand(
            GalNet.Core.Variable.VariableScope.Player,
            "route",
            GalNet.Core.Variable.VariableType.String,
            JsonSerializer.SerializeToElement("a"),
            Uid: "variable_route"));

        Assert.That(result.Success, Is.True);
        Assert.That(session.Document.Graph.PlayerVariables.Single().DefaultValue.Uid, Is.EqualTo("variable_route"));
        session.Undo();
        session.Redo();
        Assert.That(session.Document.Graph.PlayerVariables.Single().DefaultValue.Uid, Is.EqualTo("variable_route"));
    }

    [Test]
    public async Task AssetFileCommands_AreSandboxedAndUseRecoveryDelete()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "asset-command-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source.txt");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(source, "hello");
        var executor = new AssetFileCommandExecutor();
        try
        {
            var import = await executor.ExecuteAsync(root, new ImportAssetsCommand([source]));
            var escape = await executor.ExecuteAsync(root, new CreateAssetDirectoryCommand("..", "outside"));
            var delete = await executor.ExecuteAsync(root, new DeleteAssetCommand("source.txt"));

            Assert.Multiple(() =>
            {
                Assert.That(import.Success, Is.True);
                Assert.That(escape.Success, Is.False);
                Assert.That(delete.Success, Is.True);
                Assert.That(File.Exists(Path.Combine(root, "Assets", "source.txt")), Is.False);
                Assert.That(Directory.EnumerateFiles(Path.Combine(root, ".editor-trash"), "source.txt", SearchOption.AllDirectories).Any(), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static EditorSession CreateSession() => new(CreateDocument(), new MemoryPersistence());

    private static EditorProjectDocument CreateDocument() => new()
    {
        Graph = new EditorGraphDocument
        {
            Name = "Demo",
            RootNodeId = "entry",
            Nodes =
            [
                new EditorGraphNodeDto { Id = "entry", Type = "Entry", Name = "Entry" },
                new EditorGraphNodeDto { Id = "group", Type = "Group", Name = "Opening", File = "groups/group.galgroup" }
            ],
            Edges = [new EditorGraphEdgeDto { Id = "edge_entry_group", FromNodeId = "entry", FromOutlet = 0, ToNodeId = "group" }]
        },
        GroupEntries =
        {
            ["group"] =
            [
                new EditorEntryData { StableId = "opening_text", Id = 1, Type = "text", Parameters = "text=Hello" }
            ]
        }
    };

    private sealed class MemoryPersistence : IEditorSessionPersistence
    {
        public EditorProjectDocument? SavedDocument { get; private set; }

        public Task SaveAsync(EditorProjectDocument document, CancellationToken cancellationToken = default)
        {
            SavedDocument = document;
            return Task.CompletedTask;
        }
    }
}
