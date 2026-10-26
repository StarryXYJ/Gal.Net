using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task FilteredMountedEntriesRefreshForFieldsUndoTypeAndFriendlyReferenceSummaries()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "LiveGroupSearch"), "Search", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var layer = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single();
            var hide = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.HideLayer).Single(); hide.Parameters = new() { ["handleId"] = layer.Source.Parameters["handleId"].GetString()! };
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
            var view = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); TimelinePump(window);
            try
            {
                var list = view.FindControl<ListBox>("ItemsListBox")!; var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                page.SearchText = "needle"; TimelinePump(window); Assert.That(list.Items.Count, Is.Zero);
                layer.Condition = "needle"; TimelinePump(window); Assert.That(list.Items.Cast<object>(), Does.Contain(layer));
                await history.UndoAsync(); TimelinePump(window); Assert.That(list.Items.Count, Is.Zero);
                layer.Parameters = new() { ["handleId"] = layer.Source.Parameters["handleId"].GetString()!, ["assetId"] = "needle.png" };
                TimelinePump(window); Assert.That(list.Items.Cast<object>(), Does.Contain(layer));
                await history.UndoAsync(); TimelinePump(window); Assert.That(list.Items.Count, Is.Zero);
                page.SearchText = BuiltinEntryIds.Wait; TimelinePump(window); Assert.That(list.Items.Count, Is.Zero);
                page.ChangeType(hide, BuiltinEntryIds.Wait); TimelinePump(window); Assert.That(list.Items.Cast<object>(), Does.Contain(hide));
                await history.UndoAsync(); TimelinePump(window); Assert.That(list.Items.Count, Is.Zero);
                page.SearchText = ""; TimelinePump(window);
                var problems = scope.GetRequiredService<ProblemsPanelViewModel>();
                problems.Report("error", "current external issue", "test", new(group.Id, hide.StableId));
                Assert.That(hide.ProblemCount, Is.GreaterThan(0));
                var handle = layer.Source.Parameters["handleId"].GetString()!; var text = scope.GetRequiredService<IEditorLocalizationService>();
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    text.ApplyLocale(locale); TimelinePump(window);
                    Assert.That(hide.Summary, Does.Contain(group.Name)); Assert.That(hide.Summary, Does.Not.Contain(handle));
                }
                scope.GetRequiredService<GroupEntryAuthoringService>().Delete(group.Id, [layer.StableId]); TimelinePump(window);
                Assert.That(hide.Summary, Does.Contain(text["Scene.Unresolved"]));
                await history.UndoAsync(); TimelinePump(window); Assert.That(hide.Summary, Does.Contain(group.Name));
            }
            finally { window.Close(); }
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    private sealed class InputPublicationWait(TaskCompletionSource entered, TaskCompletionSource release) : IAsyncUndoableEdit
    {
        public string Description => "Hold author transaction";
        public async ValueTask RedoAsync() { entered.TrySetResult(); await release.Task; }
        public ValueTask UndoAsync() => ValueTask.CompletedTask;
    }

    [Test]
    public async Task ReadingHundredsOfReferenceSummariesDoesNotEvictAnOpenSelection()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "SummaryReadCache"), "Summary", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var layer = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single();
        var documents = scope.GetRequiredService<IEditorDocumentService>();
        for (var index = 0; index < 300; index++) documents.Groups[group.Id].Entries.Add(new()
        {
            StableId = "reference-" + index,
            Type = BuiltinEntryIds.MoveLayer,
            Parameters = new() { ["handleId"] = layer.Source.Parameters["handleId"].Clone() }
        });
        documents.SynchronizeGroups();
        var catalog = scope.GetRequiredService<SceneTargetCatalog>();
        using var selection = new SceneTargetSelection(catalog, catalog.Query(group.Id, "reference-0"));
        foreach (var entry in group.Entries.Skip(1)) Assert.That(entry.Summary, Does.Contain(group.Name));
        Assert.That(selection.IsCurrent, Is.True); await projects.CloseAsync();
    }

    [Test]
    public async Task GroupAndFileDeletionArchiveLateInvalidInputsAndKeepWorkingSavedVersionsSeparate()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "StructuralInputs"), "Inputs", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single(); await workspace.SaveAsync();
        entry.Condition = "save.unsaved";
        var inputs = scope.GetRequiredService<EntryEditSession>(); var field = scope.GetRequiredService<IEntryCatalog>().Get(entry.Type).DynamicParameters.Fields.Single();
        var state = inputs.Get(entry, field); state.Update("unfinished", true, new("invalid", "invalid"), false);
        var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = history.ExecuteAsync(new InputPublicationWait(entered, release)); await entered.Task;
        var deleting = workspace.DeleteNodeAsync(group); state.Update("latest unparsed input", true, new("invalid", "invalid"), false);
        release.SetResult(); await holding; await deleting;
        var documents = scope.GetRequiredService<IEditorDocumentService>(); var content = scope.GetRequiredService<IProjectContentSession>();
        Assert.That(inputs.PendingCount, Is.Zero); Assert.That(inputs.Archives.Single().Text, Is.EqualTo("latest unparsed input"));
        Assert.That(documents.Groups.ContainsKey(group.Id), Is.False); Assert.That(group.Entries, Is.Empty); Assert.That(await state.Commit!(), Is.Not.Null);
        await content.SaveAsync(); await history.UndoAsync();
        Assert.That(documents.Groups[group.Id].Entries.Single().Condition, Is.EqualTo("save.unsaved"));
        Assert.That(content.CaptureSaved().GroupEntries[group.Id].Single().Condition, Is.Empty);
        Assert.That(inputs.PendingCount, Is.Zero); Assert.That(inputs.CanRestore(inputs.Archives.Single()), Is.True);
        inputs.RemoveArchive(inputs.Archives.Single());
        var restored = scope.GetRequiredService<EditorGroupProjectionStore>().Get(group.Id).Entries.Single();
        var second = inputs.Get(restored, field); second.Update("invalid file-deletion input", true, new("invalid", "invalid"), false);
        var path = "Graph/" + documents.CurrentDocument.Nodes.Single(node => node.Id == group.Id).File;
        await scope.GetRequiredService<ProjectFileAuthoringService>().ExecuteAsync(new(ProjectFileAction.Delete, [path], DeleteGroups: true, AllowCandidateReferences: true));
        Assert.That(inputs.PendingCount, Is.Zero); Assert.That(inputs.Archives.Single().Text, Is.EqualTo("invalid file-deletion input"));
        await history.UndoAsync(); Assert.That(inputs.PendingCount, Is.Zero); Assert.That(inputs.CanRestore(inputs.Archives.Single()), Is.True);
        var archive = inputs.Archives.Single(); inputs.RequestRestore(archive);
        restored = scope.GetRequiredService<EditorGroupProjectionStore>().Get(group.Id).Entries.Single();
        var fresh = inputs.Get(restored, field); Assert.That(fresh.Text, Is.EqualTo("invalid file-deletion input")); Assert.That(fresh, Is.Not.SameAs(second));
        fresh.Clear(); await workspace.SaveAsync(); var root = project.RootPath; var groupId = group.Id;
        await projects.CloseAsync(); project = await projects.OpenAsync(root);
        Assert.That(project.Services.GetRequiredService<IEditorDocumentService>().Groups[groupId].Entries.Single().Condition, Is.EqualTo("save.unsaved"));
        Assert.That(project.Services.GetRequiredService<EntryEditSession>().Archives, Is.Empty); await projects.CloseAsync();
    }

    [Test]
    public async Task StructuralArchiveWriteFailurePreservesAuthorFilesLiveInputsAndHistoryGate()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "StructureArchiveFailure"), "Failure", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single(); await workspace.SaveAsync();
        var inputs = scope.GetRequiredService<EntryEditSession>(); var field = scope.GetRequiredService<IEntryCatalog>().Get(entry.Type).DynamicParameters.Fields.Single();
        var state = inputs.Get(entry, field); state.Update("pending", true, null, false);
        var content = scope.GetRequiredService<IProjectContentSession>(); var before = JsonSerializer.Serialize(content.CaptureWorking()); var saved = JsonSerializer.Serialize(content.CaptureSaved());
        var blocked = Path.Combine(project.RootPath, ".galnet/editor/input-archive.json"); Directory.CreateDirectory(blocked);
        var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        var failure = Assert.CatchAsync(async () => await workspace.DeleteNodeAsync(group));
        Assert.That(failure, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        Assert.That(JsonSerializer.Serialize(content.CaptureWorking()), Is.EqualTo(before)); Assert.That(JsonSerializer.Serialize(content.CaptureSaved()), Is.EqualTo(saved));
        Assert.That(inputs.PendingCount, Is.EqualTo(1)); Assert.That(state.IsConflicted, Is.False); Assert.That(history.IsBusy, Is.False); Assert.That(history.CanUndo, Is.False);
        Directory.Delete(blocked); state.Clear(); await projects.CloseAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference View, FieldDraftState State) DeletedProjection(IServiceProvider scope, GraphNode group, bool dirty)
    {
        var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var entry = workspace.InsertEntriesInto(group, group.Entries.Count, 1, BuiltinEntryIds.Wait).Single();
        var field = scope.GetRequiredService<IEntryCatalog>().Get(entry.Type).DynamicParameters.Fields.Single();
        var state = scope.GetRequiredService<EntryEditSession>().Get(entry, field);
        if (dirty) state.Update("preserve input", true, null, false);
        var reference = new WeakReference(entry); scope.GetRequiredService<GroupEntryAuthoringService>().Delete(group.Id, [entry.StableId]);
        return (reference, state);
    }

    [Test]
    public async Task DeletedProjectionsCanBeCollectedWhileNeutralHistoryAndArchivedInputRemain()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "CollectDeletedEntries"), "Collect", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var records = Enumerable.Range(0, 24).Select(index => DeletedProjection(scope, group, index % 2 == 0)).ToArray();
        for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.That(records.Any(record => record.View.IsAlive), Is.False); Assert.That(scope.GetRequiredService<EditorHistories>().Content.CanUndo, Is.True);
        var inputs = scope.GetRequiredService<EntryEditSession>(); Assert.That(inputs.PendingCount, Is.Zero); Assert.That(inputs.Archives.Count, Is.EqualTo(12));
        scope.GetRequiredService<EditorHistories>().Content.Clear(); await workspace.SaveAsync();
        GC.KeepAlive(records); await projects.CloseAsync();
    }
}
