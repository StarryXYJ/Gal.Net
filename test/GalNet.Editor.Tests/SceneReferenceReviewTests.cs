using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task SceneReferenceReviewIsReadOnlyRejectsStaleConfirmationAndPreservesWarningsAcrossUndoSaveReopen()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "SceneReview"), "Scene review", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services;
            var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var creator = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single();
            var handle = creator.Source.Parameters["handleId"].GetString()!;
            var consumer = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.HideLayer).Single();
            scope.GetRequiredService<EntryFieldAuthoringService>().SetParameters(group.Id, consumer.Source, consumer.Type, new() { ["handleId"] = JsonSerializer.SerializeToElement(handle) });
            var author = scope.GetRequiredService<GroupEntryAuthoringService>(); var history = scope.GetRequiredService<EditorHistories>().Content;
            var query = scope.GetRequiredService<ProjectSceneTargets>(); history.Clear();
            var preview = author.PrepareDelete(group.Id, [creator.StableId])!;
            Assert.That(history.CanUndo, Is.False); Assert.That(group.Entries, Has.Count.EqualTo(2));
            Assert.That(preview.Impact.References.Single().After.Code, Is.EqualTo("scene.reference.missing"));
            consumer.Condition = "true";
            var staleCursor = history.UndoText;
            Assert.Throws<EditorOperationException>(preview.Commit);
            Assert.That(history.UndoText, Is.EqualTo(staleCursor)); Assert.That(group.Entries, Has.Count.EqualTo(2));
            await history.UndoAsync(); history.Clear();
            var problems = scope.GetRequiredService<ProblemsPanelViewModel>(); problems.ValidateCommand.Execute(null);
            preview = author.PrepareDelete(group.Id, [creator.StableId])!; preview.Commit();
            Assert.That(query.Diagnostics().Any(issue => issue.Code == "scene.reference.missing" && issue.Owner.EntryId == consumer.StableId), Is.True);
            Assert.That(problems.Problems.Any(problem => problem.IsWarning && problem.Target?.EntryId == consumer.StableId && problem.Message == scope.GetRequiredService<IEditorLocalizationService>()["Scene.Diagnostic.scene.reference.missing"]), Is.True);
            problems.ValidateCommand.Execute(null);
            Assert.That(problems.Problems.Any(problem => problem.IsError && problem.Target?.EntryId == consumer.StableId), Is.False, "Missing scene targets are warnings, not compile/value validation errors.");
            Assert.That(problems.Problems.Single(problem => problem.SceneReference?.Code == "scene.reference.missing").SceneReference!.Revision, Is.EqualTo(query.Revision));
            Assert.Throws<EditorOperationException>(preview.Commit);
            await history.UndoAsync(); Assert.That(query.Diagnostics().Any(issue => issue.Code == "scene.reference.missing"), Is.False);
            await history.RedoAsync();
            Assert.That(consumer.Source.Parameters["handleId"].GetString(), Is.EqualTo(handle));
            await workspace.SaveAsync(); var root = project.RootPath; var groupId = group.Id; var consumerId = consumer.StableId;
            await projects.CloseAsync(); var reopened = await projects.OpenAsync(root);
            var saved = reopened.Services.GetRequiredService<IEditorDocumentService>().Groups[groupId].Entries.Single(entry => entry.StableId == consumerId);
            Assert.That(saved.Parameters["handleId"].GetString(), Is.EqualTo(handle));
            Assert.That(reopened.Services.GetRequiredService<ProjectSceneTargets>().Diagnostics().Any(issue => issue.Code == "scene.reference.missing"), Is.True);
            await projects.CloseAsync();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task GroupReviewRoutesDeleteAndTypeChangeThroughSameImpactAndCancellationKeepsHistory()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "SceneReviewUi"), "Scene review", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var creator = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowColorLayer).Single();
            var consumer = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.HideLayer).Single();
            scope.GetRequiredService<EntryFieldAuthoringService>().SetParameters(group.Id, consumer.Source, consumer.Type, new() { ["handleId"] = creator.Source.Parameters["handleId"] });
            var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
            var reviews = 0;
            page.ReviewImpactAsync = impact => { reviews++; Assert.That(impact.References, Has.Count.EqualTo(1)); return Task.FromResult(false); };
            page.SetSelection([creator]); await page.DeleteSelectionAsync();
            await page.ChangeTypeAsync(creator, BuiltinEntryIds.Wait);
            Assert.That(reviews, Is.EqualTo(2)); Assert.That(history.CanUndo, Is.False); Assert.That(creator.Type, Is.EqualTo(BuiltinEntryIds.ShowColorLayer));
            var preview = scope.GetRequiredService<GroupEntryAuthoringService>().PrepareDelete(group.Id, [creator.StableId])!;
            var dialog = SceneEntryImpactPrompt.CreateDialog(preview.Impact, scope.GetRequiredService<IEditorLocalizationService>());
            dialog.Show();
            Assert.That(dialog.GetVisualDescendants().OfType<Button>().Select(button => button.Name), Does.Contain("CommitEntryEdit").And.Contain("CancelEntryEdit"));
            dialog.Close();
            page.ReviewImpactAsync = _ => Task.FromResult(true);
            await page.ChangeTypeAsync(creator, BuiltinEntryIds.Wait);
            Assert.That(creator.Type, Is.EqualTo(BuiltinEntryIds.Wait)); Assert.That(history.CanUndo, Is.True);
            await history.UndoAsync();
            await page.DeleteSelectionAsync(); Assert.That(group.Entries, Has.Count.EqualTo(1));
            await history.UndoAsync(); Assert.That(group.Entries, Has.Count.EqualTo(2));
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }
}
