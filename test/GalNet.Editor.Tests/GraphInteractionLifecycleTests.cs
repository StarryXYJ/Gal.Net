using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [TestCase(GraphNodeKind.ChoiceBranch)]
    [TestCase(GraphNodeKind.ConditionBranch)]
    public async Task BranchStableItemsKeepTargetsThroughMoveDeleteUndoAndReopen(GraphNodeKind kind)
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "BranchRoundtrip"), "Branch", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var author = project.Services.GetRequiredService<GraphAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var branch = await workspace.AddNodeAsync(kind, 400, 200);
        author.AddBranchItem(branch.Id); author.AddBranchItem(branch.Id);
        var targets = new[] { workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup), await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 700, 300), await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 700, 500) };
        for (var i = 0; i < 3; i++) author.Connect(branch.Id, i, targets[i].Id);
        string[] Ids() => branch.Options.Count > 0 ? branch.Options.Select(item => item.Id).ToArray() : branch.Conditions.Select(item => item.Id).ToArray();
        var ids = Ids();
        var oldConnector = branch.OutputConnectors[0];
        var before = JsonSerializer.Serialize(project.Services.GetRequiredService<IProjectContentSession>().CaptureWorking());
        history.Clear();
        author.MoveBranchItem(branch.Id, ids[0], 2);
        Assert.That(oldConnector.Index, Is.EqualTo(2), "A pending connector follows the item identity.");
        Assert.That(workspace.Edges.Single(edge => edge.From == branch && edge.Outlet == 2).To.Id, Is.EqualTo(targets[0].Id));
        author.RemoveBranchItem(branch.Id, ids[1]);
        Assert.That(workspace.Edges.Where(edge => edge.From == branch).Select(edge => edge.To.Id), Is.EquivalentTo(new[] { targets[0].Id, targets[2].Id }));
        Assert.That(workspace.Edges.Single(edge => edge.From == branch && edge.Outlet == 1).To.Id, Is.EqualTo(targets[0].Id));
        await history.UndoAsync(); await history.UndoAsync();
        Assert.That(JsonSerializer.Serialize(project.Services.GetRequiredService<IProjectContentSession>().CaptureWorking()), Is.EqualTo(before));
        Assert.That(history.CanUndo, Is.False);
        await history.RedoAsync(); await history.RedoAsync();
        var field = kind == GraphNodeKind.ChoiceBranch ? "Text" : "Expression";
        author.SetBranchValue(branch.Id, ids[0], field, kind == GraphNodeKind.ChoiceBranch ? "authored {0}" : "false");
        await workspace.SaveAsync();
        var expected = Ids();
        await projects.CloseAsync();
        project = await projects.OpenAsync(project.RootPath);
        var reloaded = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        branch = reloaded.Nodes.Single(node => node.Id == branch.Id);
        Assert.That(Ids(), Is.EqualTo(expected));
        Assert.That(reloaded.Edges.Single(edge => edge.From == branch && edge.Outlet == 1).To.Id, Is.EqualTo(targets[0].Id));
        await projects.CloseAsync();
    }

    [Test]
    public async Task GraphClipboardDeepCopiesOnlyInternalEdgesAndDoesNotRewriteCustomJson()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "DeepCopy"), "Copy", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var author = project.Services.GetRequiredService<GraphAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1).Single();
        entry.Type = "dialogue.text"; entry.Parameters = new() { ["content"] = group.Id };
        entry.Source.Parameters["opaque"] = JsonSerializer.SerializeToElement(new { node = group.Id });
        WriteTranslation(project, JsonSerializer.Serialize(new Dictionary<string, string> { [group.Id] = group.Id }));
        var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 500, 300);
        author.Connect(group.Id, 0, branch.Id);
        author.Connect(branch.Id, 0, group.Id);
        workspace.SelectNodes([workspace.EntryNode!, group, branch]);
        var text = workspace.CopySelection();
        entry.Parameters = new() { ["content"] = "changed after copy" };
        history.Clear();
        var originalNodes = workspace.Nodes.Select(node => node.Id).ToArray();
        var originalEdges = workspace.Edges.Select(edge => edge.Id).ToArray();
        await workspace.PasteAsync(text, 800, 500);
        Assert.That(workspace.StatusText, Does.Contain(project.Services.GetRequiredService<IEditorLocalizationService>().Format("Status.GraphPasted", 2)));
        var pasted = workspace.SelectedNodes.ToArray();
        Assert.That(pasted, Has.Length.EqualTo(2));
        Assert.That(pasted.All(node => !originalNodes.Contains(node.Id)), Is.True);
        var copy = pasted.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        Assert.That(copy.Source.File, Is.Not.EqualTo(group.Source.File));
        Assert.That(copy.Entries.Single().StableId, Is.Not.EqualTo(entry.StableId));
        var copiedTextKey = copy.Entries.Single().Parameters["content"];
        Assert.That(copiedTextKey, Is.Not.EqualTo(group.Id), "Declared localized text gets a new identity.");
        Assert.That(project.Services.GetRequiredService<GameTextAuthoringService>().Read(copiedTextKey), Is.EqualTo(group.Id), "Text values are not guessed as scene references.");
        Assert.That(copy.Entries.Single().Source.Parameters["opaque"].GetProperty("node").GetString(), Is.EqualTo(group.Id), "Unmarked JSON stays opaque.");
        Assert.That(pasted.Single(node => node.NodeKind == GraphNodeKind.ChoiceBranch).Options.Single().Id, Is.Not.EqualTo(branch.Options.Single().Id));
        Assert.That(workspace.Edges.Count(edge => pasted.Contains(edge.From) && pasted.Contains(edge.To)), Is.EqualTo(2));
        Assert.That(workspace.Edges.Count(edge => edge.To == copy), Is.EqualTo(1), "Incoming external Entry edge is excluded.");
        var path = Path.Combine(project.RootPath, "Graph", copy.Source.File!);
        Assert.That(File.Exists(path), Is.True);
        await history.UndoAsync();
        Assert.That(workspace.Nodes.Select(node => node.Id), Is.EqualTo(originalNodes));
        Assert.That(workspace.Edges.Select(edge => edge.Id), Is.EqualTo(originalEdges));
        Assert.That(File.Exists(path), Is.False);
        Assert.That(history.CanUndo, Is.False);
        await history.RedoAsync();
        await workspace.SaveAsync();
        await projects.CloseAsync();
        var reopened = await projects.OpenAsync(project.RootPath);
        var restored = reopened.Services.GetRequiredService<IEditorDocumentService>().Groups[copy.Id].Entries.Single();
        Assert.That(restored.Parameters["content"].GetString(), Is.EqualTo(copiedTextKey));
        Assert.That(reopened.Services.GetRequiredService<GameTextAuthoringService>().Read(copiedTextKey), Is.EqualTo(group.Id));
        Assert.That(restored.Parameters["opaque"].GetProperty("node").GetString(), Is.EqualTo(group.Id));
        await projects.CloseAsync();
    }

    [Test]
    public async Task InvalidClipboardAndBranchConflictsLeaveContentAndHistoryIntact()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Rejected"), "Rejected", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var author = project.Services.GetRequiredService<GraphAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 400, 300);
        history.Clear();
        var snapshot = JsonSerializer.Serialize(project.Services.GetRequiredService<IProjectContentSession>().CaptureWorking());
        foreach (var text in new[] { "plain text", "{}", "{\"Version\":99}", "null" })
        {
            Exception? failure = null;
            try { await workspace.PasteAsync(text, 0, 0); } catch (Exception error) { failure = error; }
            Assert.That(failure, Is.TypeOf<EditorOperationException>());
            Assert.That(JsonSerializer.Serialize(project.Services.GetRequiredService<IProjectContentSession>().CaptureWorking()), Is.EqualTo(snapshot));
            Assert.That(history.CanUndo, Is.False);
        }
        author.AddBranchItem(branch.Id);
        branch.Source.Options![1].Text = "outside this edit";
        Assert.ThrowsAsync<EditorOperationException>(() => history.UndoAsync());
        Assert.That(history.UndoDescription, Does.StartWith("Edit branch"));
        Assert.That(branch.Source.Options, Has.Count.EqualTo(2));
        await projects.CloseAsync();
    }

    [Test]
    public async Task BranchInspectorTextAndGraphKeyboardUseTheCorrectEditingTarget()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "Keyboard"), "Keyboard", new(), Contracts());
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 500, 300);
            workspace.SelectNode(branch);
            var inspectorModel = project.Services.GetRequiredService<GalNet.Editor.Inspector.ViewModels.NodeInspectorControlViewModel>();
            var inspectorView = new GalNet.Editor.Inspector.Views.NodeInspectorControl { DataContext = inspectorModel };
            var graph = new NodeGraphPanelView { DataContext = workspace };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*") };
            Grid.SetColumn(inspectorView, 1); grid.Children.Add(graph); grid.Children.Add(inspectorView);
            var page = new EditorPageView { DataContext = project.Services.GetRequiredService<EditorPageViewModel>(), Content = grid };
            var window = new Window { Width = 1400, Height = 800, Content = page }; window.Show();
            try
            {
                window.UpdateLayout();
                var control = inspectorView.GetVisualDescendants().OfType<BranchOptionListEditorControl>().Single();
                var input = control.GetVisualDescendants().OfType<TextBox>().First(box => box.DataContext is BranchOptionEditorItemViewModel);
                var sentinel = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
                sentinel.Name = "Keep previous project edit";
                input.Focus(); input.SelectAll(); window.KeyTextInput("choice.new");
                Assert.That(branch.Source.Options![0].Text, Is.Empty, "Typing remains a local draft until commit.");
                Assert.That(control.GetVisualDescendants().OfType<TextBox>().Any(box => ReferenceEquals(box, input)), Is.True, "Editing must retain the active text control.");
                window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
                window.KeyRelease(Key.Z, RawInputModifiers.None, PhysicalKey.Z, "z");
                Assert.That(sentinel.Name, Is.EqualTo("Keep previous project edit"));
                Assert.That(input.Text, Is.Empty);
                var add = control.GetVisualDescendants().OfType<Button>().First(button => button.CommandParameter is null);
                add.Command!.Execute(null); window.UpdateLayout();
                Assert.That(branch.Options, Has.Count.EqualTo(2));
                var localization = project.Services.GetRequiredService<IEditorLocalizationService>();
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    localization.ApplyLocale(locale); window.UpdateLayout();
                    Assert.That(Avalonia.Automation.AutomationProperties.GetName(add), Is.EqualTo(localization["Graph.Branch.Add"]));
                    Assert.That(workspace.UndoRedoHistory.UndoText!.Render(localization), Does.Contain(branch.Name));
                }
                inspectorModel.ReorderChoiceOptionCommand.Execute(new ReorderRequest(branch.Options[0], 1));
                Assert.That(branch.Options[1].Text, Is.Empty);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                {
                    graph.FrameNodes(false); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "p3-graph-inspector.png"));
                }
                var viewport = graph.FindControl<PanZoomCanvas>("Viewport")!;
                graph.FrameNodes(false);
                foreach (var node in workspace.Nodes)
                {
                    var topLeft = viewport.WorldToViewport(new Point(node.X, node.Y));
                    var bottomRight = viewport.WorldToViewport(new Point(node.X + GraphLayoutMetrics.NodeWidth, node.Y + node.LayoutHeight));
                    Assert.That(topLeft.X, Is.GreaterThanOrEqualTo(-1)); Assert.That(topLeft.Y, Is.GreaterThanOrEqualTo(-1));
                    Assert.That(bottomRight.X, Is.LessThanOrEqualTo(viewport.Bounds.Width + 1)); Assert.That(bottomRight.Y, Is.LessThanOrEqualTo(viewport.Bounds.Height + 1));
                }
                var sourceSnapshot = JsonSerializer.Serialize(project.Services.GetRequiredService<IProjectContentSession>().CaptureWorking());
                workspace.SelectEdge(workspace.Edges.First()); workspace.SelectNode(branch, additive: true);
                Assert.That(workspace.SelectedEdge, Is.Null);
                graph.Focus();
                graph.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Control });
                Assert.That(workspace.SelectedNodes.Count, Is.EqualTo(workspace.Nodes.Count));
                graph.FrameNodes(false);
                Assert.That(JsonSerializer.Serialize(project.Services.GetRequiredService<IProjectContentSession>().CaptureWorking()), Is.EqualTo(sourceSnapshot));
                workspace.SelectNode(branch);
                await graph.CopySelectionAsync(); await graph.PasteSelectionAsync();
                Assert.That(workspace.Nodes.Count(node => node.NodeKind == GraphNodeKind.ChoiceBranch), Is.EqualTo(2));
                await workspace.UndoRedoHistory.UndoAsync();
                Assert.That(workspace.Nodes.Count(node => node.NodeKind == GraphNodeKind.ChoiceBranch), Is.EqualTo(1));
            }
            finally { window.Close(); inspectorModel.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
    [Test]
    public async Task QueuedGroupCreationFollowsItsOriginalBranchItemAfterReorder()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "QueuedSlot"), "Queued", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var author = project.Services.GetRequiredService<GraphAuthoringService>();
        var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 400, 300);
        author.AddBranchItem(branch.Id);
        var connector = branch.OutputConnectors[0];
        var release = new TaskCompletionSource();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        var blocker = history.ExecuteAsync(new CallbackEdit("Queued reorder", async () =>
        {
            await release.Task;
            branch.Source.Options!.Reverse(); documents.SynchronizeGroups();
        }, () => ValueTask.CompletedTask));
        var creating = workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 700, 300, connector);
        release.SetResult(); await blocker;
        var created = await creating;
        Assert.That(workspace.Edges.Single(edge => edge.To.Id == created.Id).Outlet, Is.EqualTo(1));
        Assert.That(branch.Options[1].Id, Is.EqualTo(connector.ItemId));
        await history.UndoAsync();
        Assert.That(workspace.Nodes.Any(node => node.Id == created.Id), Is.False);
        await projects.CloseAsync();
    }

    [TestCase(GraphNodeKind.ChoiceBranch)]
    [TestCase(GraphNodeKind.ConditionBranch)]
    public async Task EmptyBranchHasNoPhantomOutletAndCanBeRestored(GraphNodeKind kind)
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "EmptyBranch"), "Empty", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var author = project.Services.GetRequiredService<GraphAuthoringService>();
        var branch = await workspace.AddNodeAsync(kind, 400, 300);
        var target = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        author.Connect(branch.Id, 0, target.Id);
        var id = branch.OutputConnectors[0].ItemId!;
        author.RemoveBranchItem(branch.Id, id);
        Assert.That(branch.OutputConnectors, Is.Empty);
        Assert.That(workspace.Edges.Any(edge => edge.From == branch), Is.False);
        Assert.Throws<EditorOperationException>(() => author.Connect(branch.Id, 0, target.Id));
        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(branch.OutputConnectors.Single().ItemId, Is.EqualTo(id));
        Assert.That(workspace.Edges.Single(edge => edge.From == branch).To, Is.SameAs(target));
        await projects.CloseAsync();
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task PastedBatchPublicationFailureCompensatesFilesAndPreservesHistory(bool failUndo)
    {
        var fault = new PasteFaultTransaction();
        using var services = Services(transaction: fault);
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "PasteFailure"), "Failure", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var first = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var second = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 500, 300);
        workspace.SelectNodes([first, second]);
        var text = workspace.CopySelection();
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        history.Clear();
        if (failUndo) await workspace.PasteAsync(text, 800, 500);
        var before = JsonSerializer.Serialize(content.CaptureWorking());
        var beforeSaved = JsonSerializer.Serialize(content.CaptureSaved());
        var beforeUndo = history.UndoDescription;
        var files = Directory.GetFiles(project.RootPath, "*", SearchOption.AllDirectories).Where(path => !path.Contains(".galnet")).ToDictionary(path => path, File.ReadAllBytes);
        fault.Armed = true;
        Exception? failure = null;
        try { if (failUndo) await history.UndoAsync(); else await workspace.PasteAsync(text, 800, 500); } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.TypeOf<IOException>());
        Assert.That(fault.Published, Is.EqualTo(1), "Failure happens after an actual file publication.");
        Assert.That(JsonSerializer.Serialize(content.CaptureWorking()), Is.EqualTo(before));
        Assert.That(JsonSerializer.Serialize(content.CaptureSaved()), Is.EqualTo(beforeSaved));
        Assert.That(history.UndoDescription, Is.EqualTo(beforeUndo));
        Assert.That(Directory.GetFiles(project.RootPath, "*", SearchOption.AllDirectories).Where(path => !path.Contains(".galnet")), Is.EquivalentTo(files.Keys));
        foreach (var pair in files) Assert.That(File.ReadAllBytes(pair.Key), Is.EqualTo(pair.Value), pair.Key);
        if (failUndo) { await history.UndoAsync(); await history.RedoAsync(); }
        else { await workspace.PasteAsync(text, 800, 500); await history.UndoAsync(); }
        await projects.CloseAsync();
    }

    private sealed class PasteFaultTransaction : AuthorFileTransaction
    {
        public bool Armed { get; set; }
        public int Published { get; private set; }
        protected override void Publish(string path, string? stagedFile)
        {
            if (Armed && Published == 1) { Armed = false; throw new IOException("Injected second-file publication failure"); }
            base.Publish(path, stagedFile);
            if (Armed) Published++;
        }
    }

    private sealed class FaultRepository(IEditorDocumentRepository inner, AuthorFileTransaction transaction) : IEditorDocumentRepository
    {
        public LoadedEditorProjectDocument Load(string path, string name, GalNet.Core.Settings.ProjectSettings settings) => inner.Load(path, name, settings);
        public ProjectSavePlan PrepareSave(string path, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groups) => inner.PrepareSave(path, document, groups);
        public void ApplySave(string path, ProjectSavePlan plan, CancellationToken cancellationToken = default)
        {
            if (transaction is PasteFaultTransaction { Armed: true } or TextPublishFailure { Armed: true } or TextPublishPause { Armed: true } or LocalizationPublicationFault { Armed: true }) transaction.Commit(path, plan, cancellationToken);
            else inner.ApplySave(path, plan, cancellationToken);
        }
        public void Save(string path, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groups) => inner.Save(path, document, groups);
    }
    [Test]
    public async Task CreationUsesTheBranchItemIndependentlyInWorkingAndSavedOrders()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "TwoOrders"), "Orders", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var author = project.Services.GetRequiredService<GraphAuthoringService>();
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 400, 300);
        author.AddBranchItem(branch.Id);
        var original = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        author.Connect(branch.Id, 0, original.Id);
        await workspace.SaveAsync();
        var item = branch.Options[0];
        author.MoveBranchItem(branch.Id, item.Id, 1);
        var created = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 700, 300, branch.OutputConnectors[1]);
        Assert.That(workspace.Edges.Single(edge => edge.To.Id == created.Id).Outlet, Is.EqualTo(1));
        var saved = content.CaptureSaved();
        Assert.That(saved.Graph.Nodes.Single(node => node.Id == branch.Id).Options![0].Id, Is.EqualTo(item.Id), "Structural creation must not publish the pending reorder.");
        Assert.That(saved.Graph.Edges.Single(edge => edge.ToNodeId == created.Id).FromOutlet, Is.Zero, "Saved order resolves the same item at its own position.");
        Assert.That(saved.Graph.Edges.Any(edge => edge.FromNodeId == branch.Id && edge.ToNodeId == original.Id), Is.False);
        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(content.CaptureSaved().Graph.Edges.Single(edge => edge.FromNodeId == branch.Id).ToNodeId, Is.EqualTo(original.Id));
        Assert.That(workspace.Edges.Single(edge => edge.From == branch).Outlet, Is.EqualTo(1));
        await projects.CloseAsync();
    }
    [Test]
    public async Task LockedInspectorDetachesDeletedBranchAndFindsRestoredIdentity()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "LockedBranch"), "Locked", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 400, 300);
        workspace.SelectNode(branch);
        var inspector = project.Services.GetRequiredService<GalNet.Editor.Inspector.ViewModels.NodeInspectorControlViewModel>();
        inspector.SetLocked(true);
        var id = branch.Id;
        await workspace.DeleteNodeAsync(branch);
        Assert.That(inspector.HasSelectedNode, Is.False);
        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(inspector.InspectedNode!.Id, Is.EqualTo(id));
        inspector.AddChoiceOptionCommand.Execute(null);
        Assert.That(inspector.InspectedNode.Options, Has.Count.EqualTo(2));
        inspector.Dispose();
        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(workspace.Nodes.Single(node => node.Id == id).Options, Has.Count.EqualTo(1));
        await projects.CloseAsync();
    }
}
