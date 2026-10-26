using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task PendingCutMovesOncePreservesHandlesResolvesAddressCollisionsAndCopiesExpiredTickets()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "PendingEntryMove"), "Move", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
        var source = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var creator = workspace.InsertEntriesInto(source, 0, 1, BuiltinEntryIds.ShowColorLayer).Single();
        creator.Source.Parameters["future"] = JsonSerializer.Deserialize<JsonElement>("[null,9007199254740993,{\"kept\":true}]");
        var handle = creator.Source.Parameters["handleId"].GetString();
        var consumer = workspace.InsertEntriesInto(source, 1, 1, BuiltinEntryIds.HideLayer).Single();
        consumer.Parameters = new() { ["handleId"] = handle! };
        var target = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 500, 250);
        var collision = workspace.InsertEntriesInto(target, 0, 1, BuiltinEntryIds.Wait).Single();
        collision.Source.StableId = creator.StableId;
        var author = scope.GetRequiredService<GroupEntryAuthoringService>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        var notifications = new List<AuthorChangeSet>(); history.AuthorChanges.Committed += notifications.Add;
        var cut = author.PrepareCut(source.Id, [creator.StableId])!; author.BeginCut(cut);
        Assert.That(source.Entries, Has.Count.EqualTo(2)); Assert.That(history.CanUndo, Is.False);
        Assert.That(author.IsPendingMove(source.Id, creator.StableId), Is.True);
        var canceled = await author.PasteAsync(target.Id, cut.Text, null, _ => Task.FromResult(false));
        Assert.That(canceled, Is.Empty); Assert.That(author.HasPendingMove, Is.True); Assert.That(history.CanUndo, Is.False);
        var moved = (await author.PasteAsync(target.Id, cut.Text, null)).Single();
        Assert.That(moved.StableId, Is.Not.EqualTo(creator.StableId)); Assert.That(moved.Source.Parameters["handleId"].GetString(), Is.EqualTo(handle));
        Assert.That(consumer.Source.Parameters["handleId"].GetString(), Is.EqualTo(handle)); Assert.That(author.HasPendingMove, Is.False);
        Assert.That(moved.Source.Parameters["future"][1].GetInt64(), Is.EqualTo(9007199254740993L));
        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Moves.Single(), Is.EqualTo(new AuthorEntryMove(source.Id, creator.StableId, target.Id, moved.StableId)));
        await history.UndoAsync(); Assert.That(source.Entries[0], Is.SameAs(creator)); Assert.That(target.Entries, Has.Count.EqualTo(1));
        Assert.That(notifications.Last().Moves.Single().SourceGroupId, Is.EqualTo(target.Id));
        await history.RedoAsync(); Assert.That(target.Entries.Last().StableId, Is.EqualTo(moved.StableId));
        var repeated = (await author.PasteAsync(target.Id, cut.Text, null)).Single();
        Assert.That(author.LastPasteCopiedMove, Is.True); Assert.That(repeated.Source.Parameters["handleId"].GetString(), Is.Not.EqualTo(handle));
        var next = author.PrepareCut(target.Id, [moved.StableId])!; author.BeginCut(next);
        moved.Condition = "true"; Assert.That(author.HasPendingMove, Is.False);
        var copied = (await author.PasteAsync(source.Id, next.Text, null)).Single();
        Assert.That(author.LastPasteCopiedMove, Is.True); Assert.That(copied.Source.Parameters["handleId"].GetString(), Is.Not.EqualTo(handle));
        await workspace.SaveAsync(); var path = project.RootPath; var targetId = target.Id; var movedId = moved.StableId;
        await projects.CloseAsync(); project = await projects.OpenAsync(path);
        Assert.That(project.Services.GetRequiredService<IEditorDocumentService>().Groups[targetId].Entries.Single(entry => entry.StableId == movedId).Parameters["handleId"].GetString(), Is.EqualTo(handle));
        await projects.CloseAsync();
        project = await projects.CreateAsync(Path.Combine(_root, "ForeignMoveProject"), "Foreign", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var foreignGroup = project.Services.GetRequiredService<EditorWorkspaceViewModel>().Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var foreignAuthor = project.Services.GetRequiredService<GroupEntryAuthoringService>();
        var foreign = (await foreignAuthor.PasteAsync(foreignGroup.Id, next.Text, null)).Single();
        Assert.That(foreignAuthor.LastPasteCopiedMove, Is.True); Assert.That(foreign.Source.Parameters["handleId"].GetString(), Is.Not.EqualTo(handle));
        await projects.CloseAsync();
    }

    [Test]
    public async Task DeletedInputDoesNotBlockSaveAndUndoRestoresOnlyThroughNewBindingWithDurableRecovery()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "InputArchive"), "Inputs", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
            var inputs = scope.GetRequiredService<EntryEditSession>(); var field = entry.DetailForm!.Fields.Single();
            var oldBinding = entry.DetailForm.Binding(field); var old = oldBinding.Draft!;
            old.Update("2.5", true, null, false);
            var author = scope.GetRequiredService<GroupEntryAuthoringService>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var preview = author.PrepareDelete(group.Id, [entry.StableId])!;
            Assert.That(preview.Impact.ArchivedInputs, Is.EqualTo(1));
            old.Update("3.5", true, null, false); Assert.Throws<EditorOperationException>(preview.Commit);
            author.Delete(group.Id, [entry.StableId]);
            Assert.That(inputs.PendingCount, Is.Zero); Assert.That(inputs.Archives.Single().Text, Is.EqualTo("3.5"));
            Assert.That(old.CanCommit, Is.False); Assert.That(await old.Commit!(), Is.Not.Null);
            await workspace.SaveAsync(); Assert.That(scope.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
            await history.UndoAsync(); Assert.That(inputs.PendingCount, Is.Zero); Assert.That(oldBinding.Draft, Is.Null);
            var archive = inputs.Archives.Single(); Assert.That(inputs.CanRestore(archive), Is.True);
            var problems = scope.GetRequiredService<ProblemsPanelViewModel>();
            var recovery = problems.Problems.Single(problem => problem.InputText == "3.5"); Assert.That(recovery.HasRestore, Is.True);
            var view = new ProblemsPanelView { DataContext = problems }; var window = new Window { Content = view, Width = 700, Height = 450 }; window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.That(view.GetVisualDescendants().OfType<TextBox>().Any(input => input.IsReadOnly && input.Text == "3.5"), Is.True); window.Close();
            inputs.RequestRestore(archive);
            var fresh = entry.DetailForm!.Binding(entry.DetailForm.Fields.Single()).Draft!;
            Assert.That(fresh, Is.Not.SameAs(old)); Assert.That(fresh.Text, Is.EqualTo("3.5")); Assert.That(fresh.IsConflicted, Is.False);
            Assert.That(inputs.Archives, Is.Empty); Assert.That(inputs.PendingCount, Is.EqualTo(1));
            await workspace.SaveAsync(); Assert.That(entry.Source.Parameters[field.Id].GetSingle(), Is.EqualTo(3.5f));
            fresh.Update("invalid retained input", true, new ValueIssue("value.type", "invalid"), false);
            author.Delete(group.Id, [entry.StableId]); await workspace.SaveAsync(); history.Clear();
            Assert.That(inputs.Archives.Single().Text, Is.EqualTo("invalid retained input"));
            var path = project.RootPath; await projects.CloseAsync(); project = await projects.OpenAsync(path);
            var reopened = project.Services.GetRequiredService<EntryEditSession>();
            Assert.That(reopened.Archives.Single().Text, Is.EqualTo("invalid retained input")); Assert.That(reopened.PendingCount, Is.Zero);
            reopened.RemoveArchive(reopened.Archives.Single()); await projects.CloseAsync();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task InputArchiveBudgetAndIoFailureRejectBeforeDeletingAuthorOrDetachingInput()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "InputLimits"), "Inputs", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
        using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
        var content = scope.GetRequiredService<IProjectContentSession>(); var documents = scope.GetRequiredService<IEditorDocumentService>();
        var texts = scope.GetRequiredService<GameTextAuthoringService>(); var history = scope.GetRequiredService<EditorHistories>(); history.Clear();
        using (var limited = new EntryEditSession(content, texts, scope.GetRequiredService<IEditorLocalizationService>(), documents, projects, new(0, 1024)))
        {
            var state = limited.Get(entry, entry.DetailForm!.Fields.Single()); state.Update("retained", true, null, false);
            using var author = new GroupEntryAuthoringService(scope.GetRequiredService<EditorGroupProjectionStore>(), documents, scope.GetRequiredService<IEntryCatalog>(), history, texts, limited);
            Assert.Throws<EditorOperationException>(() => author.Delete(group.Id, [entry.StableId]));
            Assert.That(group.Entries, Has.Count.EqualTo(1)); Assert.That(limited.PendingCount, Is.EqualTo(1)); Assert.That(limited.Archives, Is.Empty);
            Assert.That(history.Content.CanUndo, Is.False); state.Clear();
        }
        var inputs = scope.GetRequiredService<EntryEditSession>(); var draft = inputs.Get(entry, entry.DetailForm!.Fields.Single()); draft.Update("4", true, null, false);
        var archivePath = Path.Combine(project.RootPath, ".galnet", "editor", "input-archive.json"); Directory.CreateDirectory(archivePath);
        var failure = Assert.Catch(() => scope.GetRequiredService<GroupEntryAuthoringService>().Delete(group.Id, [entry.StableId]));
        Assert.That(failure, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        Assert.That(group.Entries, Has.Count.EqualTo(1)); Assert.That(inputs.PendingCount, Is.EqualTo(1)); Assert.That(inputs.Archives, Is.Empty);
        Assert.That(history.Content.CanUndo, Is.False); Assert.That(draft.CanCommit, Is.True);
        Directory.Delete(archivePath); draft.Clear();
        File.WriteAllText(archivePath, "{broken recovery");
        using (var corrupt = new EntryEditSession(content, texts, scope.GetRequiredService<IEditorLocalizationService>(), documents, projects))
        {
            var retained = corrupt.Get(entry, entry.DetailForm!.Fields.Single()); retained.Update("preserved after corruption", true, null, false);
            using var author = new GroupEntryAuthoringService(scope.GetRequiredService<EditorGroupProjectionStore>(), documents, scope.GetRequiredService<IEntryCatalog>(), history, texts, corrupt);
            Assert.Throws<EditorOperationException>(() => author.Delete(group.Id, [entry.StableId]));
            Assert.That(corrupt.ArchiveError, Is.Not.Null); Assert.That(corrupt.PendingCount, Is.EqualTo(1)); Assert.That(group.Entries, Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(archivePath), Is.EqualTo("{broken recovery")); retained.Clear();
        }
        File.Delete(archivePath); await projects.CloseAsync();
    }

    [Test]
    public async Task FailedCompositeRestoresLiveInputsWhileSuccessfulTypeUndoOnlyOffersArchivedInput()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "InputCompensation"), "Inputs", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
        using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
        var inputs = scope.GetRequiredService<EntryEditSession>(); var state = inputs.Get(entry, entry.DetailForm!.Fields.Single()); state.Update("7", true, null, false);
        var fields = scope.GetRequiredService<EntryFieldAuthoringService>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        var operation = new EntryDraftLifecycleEdit(inputs, new DelegateEdit("type", () => entry.Source.Type = BuiltinEntryIds.Wait, () => entry.Source.Type = BuiltinEntryIds.HideLayer),
            [new(new(group.Id, entry.StableId), new(group.Id, entry.StableId))], history.AuthorChanges);
        Assert.Throws<IOException>(() => history.Execute(new CompositeEdit("fail", [operation, new DelegateEdit("fail", () => { }, () => throw new IOException("injected"))])));
        Assert.That(entry.Type, Is.EqualTo(BuiltinEntryIds.Wait)); Assert.That(inputs.PendingCount, Is.EqualTo(1)); Assert.That(inputs.Archives, Is.Empty);
        Assert.That(state.CanCommit, Is.True); Assert.That(state.IsConflicted, Is.False); Assert.That(history.CanUndo, Is.False); Assert.That(history.RepairRequired, Is.False);
        fields.SetType(group.Id, entry.Source, BuiltinEntryIds.HideLayer, new());
        Assert.That(inputs.PendingCount, Is.Zero); Assert.That(inputs.Archives.Single().Text, Is.EqualTo("7"));
        await history.UndoAsync(); Assert.That(inputs.PendingCount, Is.Zero); Assert.That(inputs.CanRestore(inputs.Archives.Single()), Is.True);
        Assert.That(state.CanCommit, Is.False); await workspace.SaveAsync(); await projects.CloseAsync();
    }

    [Test]
    public async Task MoveArchivesFollowBothAddressDirectionsAndExpiredOfferCannotReviveAfterUndo()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "MovingInputs"), "Inputs", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var source = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entries = workspace.InsertEntriesInto(source, 0, 3, BuiltinEntryIds.Wait).ToArray(); var target = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 500, 200);
        using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, source.Id);
        var author = scope.GetRequiredService<GroupEntryAuthoringService>(); var inputs = scope.GetRequiredService<EntryEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        var offer = author.PrepareCut(source.Id, [entries[0].StableId])!;
        entries[0].Condition = "true"; await history.UndoAsync();
        Assert.Throws<EditorOperationException>(() => author.BeginCut(offer), "Equal JSON restored by Undo cannot revive a clipboard offer.");
        history.Clear(); offer = author.PrepareCut(source.Id, [entries[0].StableId])!; author.BeginCut(offer);
        await author.PasteAsync(source.Id, offer.Text, entries[2].StableId);
        Assert.That(source.Entries.Last(), Is.SameAs(entries[0])); await history.UndoAsync(); Assert.That(source.Entries, Is.EqualTo(entries));
        history.Clear(); var old = inputs.Get(entries[0], entries[0].DetailForm!.Fields.Single()); old.Update("6", true, null, false);
        offer = author.PrepareCut(source.Id, [entries[0].StableId])!; author.BeginCut(offer);
        var moved = (await author.PasteAsync(target.Id, offer.Text, null)).Single();
        Assert.That(inputs.PendingCount, Is.Zero); Assert.That(inputs.Archives.Single().Owner.GroupId, Is.EqualTo(target.Id));
        Assert.That(inputs.CanRestore(inputs.Archives.Single()), Is.True); Assert.That(old.CanCommit, Is.False);
        await history.UndoAsync(); Assert.That(inputs.Archives.Single().Owner.GroupId, Is.EqualTo(source.Id));
        Assert.That(inputs.CanRestore(inputs.Archives.Single()), Is.True);
        await history.RedoAsync(); Assert.That(inputs.Archives.Single().Owner.EntryId, Is.EqualTo(moved.StableId));
        var archive = inputs.Archives.Single();
        using var targetPage = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, target.Id);
        inputs.RequestRestore(archive); var fresh = moved.DetailForm!.Binding(moved.DetailForm.Fields.Single()).Draft!;
        Assert.That(fresh.Text, Is.EqualTo("6")); Assert.That(fresh.CanCommit, Is.True); Assert.That(fresh.IsConflicted, Is.False);
        await workspace.SaveAsync(); await projects.CloseAsync();
    }

    [Test]
    public async Task NestedAndLocalizedArchivedInputsUseTheirOriginalRootBaselineAndNewChildBindings()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "NestedArchivedInputs"), "Inputs", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var layer = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowColorLayer).Single();
        var text = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.Text).Single();
        text.Parameters = new() { ["content"] = "" };
        using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
        var inputs = scope.GetRequiredService<EntryEditSession>(); var author = scope.GetRequiredService<GroupEntryAuthoringService>(); var history = scope.GetRequiredService<EditorHistories>().Content;
        var transform = layer.DetailForm!.Fields.Single(field => field.Id == "transform");
        var root = inputs.Get(layer, transform); var childField = new FieldDescriptor("x", new(typeof(float)));
        var child = root.Child!(childField, "x"); child.Update("8.25", true, null, false);
        var content = text.DetailForm!.Fields.Single(field => field.Id == "content"); inputs.Get(text, content).Update("recovered localized text", true, null, false);
        history.Clear(); author.Delete(group.Id, [layer.StableId, text.StableId]);
        Assert.That(inputs.Archives, Has.Count.EqualTo(2)); Assert.That(inputs.Archives.All(record => !inputs.CanRestore(record)), Is.True);
        await history.UndoAsync(); Assert.That(inputs.Archives.All(inputs.CanRestore), Is.True);
        foreach (var record in inputs.Archives.ToArray()) inputs.RequestRestore(record);
        var newRoot = inputs.Get(layer, layer.DetailForm!.Fields.Single(field => field.Id == "transform"));
        var restoredChild = newRoot.Child!(childField, "x");
        Assert.That(restoredChild.Text, Is.EqualTo("8.25")); Assert.That(restoredChild.IsConflicted, Is.False); Assert.That(child.CanCommit, Is.False);
        var restoredText = inputs.Get(text, text.DetailForm!.Fields.Single(field => field.Id == "content"));
        Assert.That(restoredText.Text, Is.EqualTo("recovered localized text")); Assert.That(inputs.Archives, Is.Empty);
        await workspace.SaveAsync();
        Assert.That(layer.Source.Parameters["transform"].EnumerateObject().Single(property => property.Name.Equals("x", StringComparison.OrdinalIgnoreCase)).Value.GetSingle(), Is.EqualTo(8.25f));
        var key = text.Source.Parameters["content"].GetString()!;
        Assert.That(scope.GetRequiredService<GameTextAuthoringService>().Read(key), Is.EqualTo("recovered localized text"));
        await projects.CloseAsync();
    }

    [TestCase(false), TestCase(true)]
    public async Task InputRollbackFailureAlwaysReleasesHistoryGateAndRequiresRepair(bool asynchronous)
    {
        using var history = new UndoRedoHistory();
        var edit = new DelegateEdit("injected rollback", () => { }, () =>
        {
            history.AuthorChanges.OnRollback(() => throw new IOException("recovery rollback failed"));
            throw new IOException("author step failed");
        });
        if (asynchronous) Assert.ThrowsAsync<HistoryRepairRequiredException>(async () => await history.ExecuteAsync(edit));
        else Assert.Throws<HistoryRepairRequiredException>(() => history.Execute(edit));
        Assert.That(history.RepairRequired, Is.True); Assert.That(history.IsBusy, Is.False); Assert.That(history.CanUndo, Is.False);
        var blocked = Assert.Throws<InvalidOperationException>(() => history.Execute(new DelegateEdit("later", () => { }, () => { })));
        Assert.That(blocked!.Message, Does.Contain("repair"));
        await Task.CompletedTask;
    }
}
