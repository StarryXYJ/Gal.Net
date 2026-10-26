using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task ExternalFileWatcherUpdatesTheMountedGroupAndRetainsItsPageSelectionAndScope()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "LiveExternal"), "Live", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
            await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
            var dock = project.Services.GetRequiredService<EditorDockFactory>(); var layout = dock.CreateLayout(); dock.InitLayout(layout); dock.OpenGroupEditor(group);
            var page = TimelineDockables(layout).Select(item => item.Context).OfType<GroupEditorPanelViewModel>().Single(); page.SelectedEntry = entry;
            var monitor = project.Services.GetRequiredService<ProjectExternalChanges>();
            await monitor.CheckNowAsync(); monitor.Start();
            var view = new GroupEditorPanelView { DataContext = page }; var window = new Window { Width = 1100, Height = 700, Content = view }; window.Show();
            try
            {
                var path = Path.Combine(project.RootPath, "Graph", group.Source.File!);
                var json = JsonNode.Parse(File.ReadAllText(path))!; json["entries"]![0]!["parameters"]!["duration"] = 2;
                var inserted = json["entries"]![0]!.DeepClone(); inserted["id"] = "externally-inserted";
                json["entries"]!.AsArray().Insert(0, inserted);
                File.WriteAllText(path, json.ToJsonString());
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                while (monitor.State != ExternalProjectState.Reloaded) await Task.Delay(50, timeout.Token);
                window.UpdateLayout();
                Assert.That(projects.Current, Is.SameAs(project)); Assert.That(workspace.Nodes.Single(node => node.Id == group.Id), Is.SameAs(group));
                Assert.That(page.SelectedEntry, Is.SameAs(entry)); Assert.That(page.IsDisposed, Is.False);
                Assert.That(entry.Source.Parameters["duration"].GetSingle(), Is.EqualTo(2));
                Assert.That(entry.Parameters["duration"], Is.EqualTo("2"));
                Assert.That(entry.Id, Is.EqualTo(2)); Assert.That(group.Entries, Has.Count.EqualTo(2));
                Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
                Assert.That(project.Services.GetRequiredService<EditorHistories>().Content.CanUndo, Is.False);
            }
            finally { window.Close(); await projects.CloseAsync(); }
            Assert.That(await monitor.CheckNowAsync(), Is.EqualTo(ExternalReloadResult.Unavailable));
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task ExternalReloadPreservesSettingsDraftUntilExplicitDiscardAndRefreshesOpenTextEncoding()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "ExternalConflict"), "Original", new(), Contracts());
            var content = project.Services.GetRequiredService<IProjectContentSession>();
            var settings = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
            var notes = Path.Combine(project.RootPath, "notes.txt"); File.WriteAllText(notes, "before");
            var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open("notes.txt");
            var monitor = project.Services.GetRequiredService<ProjectExternalChanges>(); await monitor.CheckNowAsync();
            await Stage(settings, "defaultWidth", 1700); buffer.Text = "unsaved input";
            var edited = await new ProjectEditService(project.RootPath, project.Services.GetRequiredService<IEntryCatalog>()).ExecuteAsync([new GalNet.Editor.Abstraction.Commands.RenameProjectCommand("External")]);
            Assert.That(edited.Success, Is.True);
            File.WriteAllText(notes, "外部 text", Encoding.Unicode);
            Assert.That(await monitor.CheckNowAsync(), Is.EqualTo(ExternalReloadResult.LocalChanges));
            Assert.That(Setting(settings, "defaultWidth").Read().Json!.Value.GetInt32(), Is.EqualTo(1700)); Assert.That(buffer.Text, Is.EqualTo("unsaved input"));
            Assert.That(await monitor.CheckNowAsync(true), Is.EqualTo(ExternalReloadResult.Reloaded));
            Assert.That(settings.ProjectName, Is.EqualTo("External")); Assert.That(Setting(settings, "defaultWidth").Read().Json!.Value.GetInt32(), Is.EqualTo(1920));
            Assert.That(buffer.Text, Is.EqualTo("外部 text")); Assert.That(buffer.Information, Is.EqualTo("utf-16"));
            Assert.That(content.IsDirty, Is.False);
            buffer.Text = "saved after reload"; await content.SaveAsync();
            Assert.That(File.ReadAllText(notes), Is.EqualTo("saved after reload"));
            Assert.That(await monitor.CheckNowAsync(), Is.EqualTo(ExternalReloadResult.Unchanged));
            File.WriteAllBytes(notes, [0, 1, 2]); await monitor.CheckNowAsync();
            Assert.That(buffer.IsReadOnly, Is.True); Assert.That(File.ReadAllBytes(notes), Is.EqualTo(new byte[] { 0, 1, 2 }));
            File.WriteAllText(notes, "text again"); await monitor.CheckNowAsync();
            Assert.That(buffer.IsReadOnly, Is.False); Assert.That(buffer.Text, Is.EqualTo("text again"));
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task InvalidExternalFileRecoversAfterGitStyleRestorationWithoutReplacingTheProject()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "ExternalInvalid"), "Original", new(), Contracts());
        var monitor = project.Services.GetRequiredService<ProjectExternalChanges>(); await monitor.CheckNowAsync();
        var path = Path.Combine(project.RootPath, "Graph/graph.json"); var bytes = File.ReadAllBytes(path);
        File.WriteAllText(path, "{"); Assert.That(await monitor.CheckNowAsync(), Is.EqualTo(ExternalReloadResult.Retry));
        Assert.That(monitor.State, Is.EqualTo(ExternalProjectState.Invalid));
        Assert.That(project.Services.GetRequiredService<IEditorDocumentService>().CurrentDocument.Name, Is.EqualTo("Original"));
        File.WriteAllBytes(path, bytes); Assert.That(await monitor.CheckNowAsync(), Is.EqualTo(ExternalReloadResult.Unchanged));
        Assert.That(monitor.HasIssue, Is.False); Assert.That(projects.Current, Is.SameAs(project));
        await projects.CloseAsync();
    }

    [Test]
    public async Task DiscardingExternalConflictKeepsMountedEntryDraftRegistrationAndWaitsForComposition()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "ExternalEntryDraft"), "Draft", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var content = project.Services.GetRequiredService<IProjectContentSession>(); await content.SaveAsync();
            var edits = project.Services.GetRequiredService<EntryEditSession>(); var draft = edits.Get(entry, entry.DetailForm!.Fields.Single(field => field.Id == "duration"));
            draft.Update("3", true, null, false);
            var edited = await new ProjectEditService(project.RootPath, project.Services.GetRequiredService<IEntryCatalog>()).ExecuteAsync([new GalNet.Editor.Abstraction.Commands.RenameProjectCommand("External")]);
            Assert.That(edited.Success, Is.True);
            var monitor = project.Services.GetRequiredService<ProjectExternalChanges>();
            using (draft.RegisterGuard(() => false))
                Assert.That(await monitor.CheckNowAsync(true), Is.EqualTo(ExternalReloadResult.LocalChanges));
            Assert.That(await monitor.CheckNowAsync(true), Is.EqualTo(ExternalReloadResult.Reloaded));
            draft.Update("4", true, null, false); await content.SaveAsync();
            Assert.That(entry.Source.Parameters["duration"].GetSingle(), Is.EqualTo(4)); Assert.That(edits.PendingCount, Is.Zero);
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task AssetNotificationsWaitForTheWholeExternalPublication()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "ExternalAssets"), "Assets", new(), Contracts());
        var assets = services.GetRequiredService<GalNet.Editor.Services.Interfaces.IAssetCatalogService>();
        await assets.RefreshAsync();
        var count = 0; assets.Changed += () => count++;
        var transaction = Path.Combine(project.RootPath, ".galnet/editor/transactions/external"); Directory.CreateDirectory(transaction);
        var manifest = Path.Combine(transaction, "manifest.json"); File.WriteAllText(manifest, "{\"status\":\"publishing\"}");
        var refreshing = assets.RefreshAsync();
        Assert.That(refreshing.IsCompleted, Is.False); Assert.That(count, Is.Zero);
        File.WriteAllText(manifest, "{\"status\":\"committed\"}");
        await refreshing.WaitAsync(TimeSpan.FromSeconds(5)); Assert.That(count, Is.EqualTo(1));
        await projects.CloseAsync();
    }
}
