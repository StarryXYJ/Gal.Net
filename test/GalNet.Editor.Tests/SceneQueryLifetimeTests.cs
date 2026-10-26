using System.Text.Json;
using Avalonia.Headless;
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
    public async Task UnchangedQueryRetainsTimelineInputAndChangedThenUndoneTargetCannotReviveSelector()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "QueryLifetime"), "Lifetime", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var layer = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single();
            var timeline = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.PlayTimeline).Single();
            var later = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.Wait).Single();
            var author = scope.GetRequiredService<EntryFieldAuthoringService>();
            author.SetParameters(group.Id, timeline.Source, timeline.Type, new() { ["timeline"] = JsonSerializer.SerializeToElement(new
            {
                playbackHandleId = "p", durationFrames = 60, tracks = new[] { new { handleId = layer.Source.Parameters["handleId"].GetString(), property = "opacity", keys = new[] { new { frame = 0, value = 1 } } } }, events = Array.Empty<object>()
            }) });
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = timeline;
            var session = scope.GetRequiredService<TimelineEditSession>(); var query = scope.GetRequiredService<ProjectSceneTargets>();
            var row = session.Rows.Single(item => item.HasTrack); row.DraftValue = .25m;
            var catalog = scope.GetRequiredService<SceneTargetCatalog>();
            using var selector = new SceneTargetSelection(catalog, catalog.Query(group.Id, timeline.StableId));
            selector.Select(selector.Items.First().Id);
            var captures = query.AuthorCaptureCount; var transfers = query.AnalysisEntryTransferCount;
            for (var i = 0; i < 100; i++) catalog.Query(group.Id, timeline.StableId);
            Assert.That(query.AuthorCaptureCount, Is.EqualTo(captures)); Assert.That(query.AnalysisEntryTransferCount, Is.EqualTo(transfers));
            author.SetCondition(group.Id, later.Source, "false");
            Assert.That(selector.IsCurrent, Is.True); Assert.That(session.Rows.Single(item => item.HasTrack), Is.SameAs(row));
            Assert.That(row.DraftValue, Is.EqualTo(.25m)); Assert.That(row.Commit(), Is.True);
            author.SetCondition(group.Id, layer.Source, "false");
            Assert.That(selector.IsCurrent, Is.False); await scope.GetRequiredService<EditorHistories>().Content.UndoAsync();
            Assert.That(selector.IsCurrent, Is.False); Assert.That(row.Commit(), Is.False);
            using var replaced = new SceneTargetSelection(catalog, catalog.Query(group.Id, timeline.StableId));
            var documents = scope.GetRequiredService<IEditorDocumentService>();
            var entries = documents.Groups[group.Id].Entries; var index = entries.IndexOf(timeline.Source);
            entries[index] = JsonSerializer.Deserialize<GalNet.Editor.Abstraction.Documents.EditorEntryData>(JsonSerializer.Serialize(timeline.Source))!;
            documents.SynchronizeGroups();
            Assert.That(replaced.IsCurrent, Is.False, "Equal JSON cannot renew the identity of a replaced writing context.");
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }
}
