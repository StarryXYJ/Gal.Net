using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task TimelineEventQueryPickerKeepsPositionAndPropertyCreationUsesFrameZeroBaseline()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TimelineEventQuery"), "Events", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var layer = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single();
            layer.Source.Parameters["transform"] = JsonSerializer.SerializeToElement(new { x = 4 });
            var handle = layer.Source.Parameters["handleId"].GetString();
            var timeline = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.PlayTimeline).Single();
            timeline.Source.Parameters["timeline"] = JsonSerializer.SerializeToElement(new
            {
                durationFrames = 20,
                future = new { preserved = true },
                tracks = Array.Empty<object>(),
                events = new object[]
                {
                    new { frame = 5, type = BuiltinEntryIds.MoveLayer, parameters = new { handleId = handle, transform = new { x = 20 } } },
                    new { frame = 15, type = BuiltinEntryIds.ShowLayer, editorId = "creator", parameters = new { handleId = "future-object", assetId = "future.png" } }
                }
            });
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = timeline;
            var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var localization = scope.GetRequiredService<IEditorLocalizationService>(); var query = scope.GetRequiredService<ProjectSceneTargets>();
            var revision = query.Revision; var original = timeline.Source.Parameters["timeline"].GetRawText();
            var view = scope.GetRequiredService<TimelinePanelView>(); view.DataContext = scope.GetRequiredService<TimelinePanelViewModel>();
            var window = new Window { Content = view, Width = 1350, Height = 950 }; window.Show(); TimelinePump(window);
            try
            {
                var button = view.FindControl<Button>("AddTimelineObject")!; TimelineClick(window, button);
                await PumpCharacterUi(() => FlyoutBase.GetAttachedFlyout(button) is Flyout { Content: SceneTargetPicker });
                var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(button)!; var picker = (SceneTargetPicker)flyout.Content!;
                var draft = session.ObjectEdit!; var future = draft.Snapshot.Choices.Single(choice => choice.Target.IsFuture);
                Assert.That(future.Item.Badge, Is.EqualTo(localization.Format("Scene.FutureAt", 15)));
                await draft.PrepareAsync(); Assert.That(draft.Snapshot.Position, Is.EqualTo(new SceneTimelinePosition(0, IncludeFuture: true)));
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    localization.ApplyLocale(locale); draft.Selection.RefreshLabels(); TimelinePump(window);
                    Assert.That(draft.Snapshot.Choices.Single(choice => choice.Target.IsFuture).Item.Badge, Is.EqualTo(localization.Format("Scene.FutureAt", 15)));
                }
                picker.ResourcePanel.Search.Text = "future.png"; TimelinePump(window); PressGroupKey(picker.ResourcePanel.Search, Key.Down); PressGroupKey(picker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                Assert.That(session.VisibleNodes.Any(node => node.ObjectHandle == "future-object"), Is.True);
                Assert.That(history.CanUndo, Is.False); Assert.That(timeline.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original));
                var stale = session.BeginObjectEdit()!; stale.Select(stale.Snapshot.Choices.First().Item.Id); session.Frame = 10;
                Assert.That(stale.Commit(), Is.False); Assert.That(query.Revision, Is.EqualTo(revision));
                var row = session.Tree.Single(node => node.ObjectHandle == handle).Children.SelectMany(node => node.Children.Prepend(node)).Select(node => node.Value).Single(item => item?.Property == "transform.x")!;
                Assert.That(row.DraftValue, Is.EqualTo(20)); Assert.That(session.AddProperty(row), Is.True);
                var keys = timeline.Source.Parameters["timeline"].GetProperty("tracks")[0].GetProperty("keys");
                Assert.That(keys[0].GetProperty("frame").GetInt32(), Is.Zero); Assert.That(keys[0].GetProperty("value").GetSingle(), Is.EqualTo(4));
                Assert.That(keys[1].GetProperty("frame").GetInt32(), Is.EqualTo(10)); Assert.That(keys[1].GetProperty("value").GetSingle(), Is.EqualTo(20));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(timeline.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original)); await history.RedoAsync();
                session.Frame = 15; Assert.That(session.AnalysisIncomplete, Is.False);
                var created = session.BeginObjectEdit()!; Assert.That(created.Snapshot.Choices.Single(choice => choice.Target.Handle == "future-object").Target.Presence, Is.EqualTo(TargetPresence.Definite));
                var root = project.RootPath; var groupId = group.Id; var entryId = timeline.StableId;
                await workspace.SaveAsync(); Assert.That(created.IsCurrent, Is.True); window.Close(); page.Dispose(); await projects.CloseAsync(); Assert.That(created.IsCurrent, Is.False);
                project = await projects.OpenAsync(root);
                var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == entryId).Source.Parameters["timeline"];
                Assert.That(reopened.GetProperty("future").GetProperty("preserved").GetBoolean(), Is.True);
                Assert.That(reopened.GetProperty("events")[1].GetProperty("editorId").GetString(), Is.EqualTo("creator"));
                Assert.That(reopened.GetProperty("tracks")[0].GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(4));
                await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
