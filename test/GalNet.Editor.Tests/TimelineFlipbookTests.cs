using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    public async Task FlipbookPropertyUsesRealMenuAndNumericInputWithRawIndicesHistoryInvalidationAndReopen()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "FlipbookTimeline"), "Flipbook", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var layers = workspace.InsertEntriesInto(group, 0, 2, BuiltinEntryIds.ShowLayer); var layer = layers[0]; var handle = layer.Source.Parameters["handleId"].GetString();
            layer.Source.Parameters["flipbook"] = JsonSerializer.Deserialize<JsonElement>("{\"columns\":3,\"rows\":2,\"frameCount\":5,\"index\":2.5,\"future\":[9007199254740993,null]}");
            var timeline = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.PlayTimeline).Single();
            timeline.Source.Parameters["timeline"] = JsonSerializer.Deserialize<JsonElement>("{\"durationFrames\":20,\"tracks\":[],\"events\":[],\"future\":true}");
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = timeline;
            var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var view = scope.GetRequiredService<GalNet.Editor.Views.TimelinePanelView>(); view.DataContext = scope.GetRequiredService<TimelinePanelViewModel>();
            var window = new Window { Content = view, Width = 1350, Height = 950 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(session.AnalysisIncomplete, Is.False);
                Assert.That(session.Tree.Single(node => node.ObjectHandle != handle).Children.SelectMany(node => node.Children.Append(node)).Any(node => node.Value?.Property == "flipbook.index"), Is.False);
                var draft = session.BeginObjectEdit()!; draft.Select(draft.Snapshot.Choices.Single(choice => choice.Target.Handle == handle).Item.Id); Assert.That(draft.Commit(), Is.True);
                session.Frame = 10; TimelinePump(window);
                var plus = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "AddTimelineProperty" && control.IsVisible && control.DataContext is TimelineTreeNode node && node.ObjectHandle == handle);
                TimelineClick(window, plus); var menu = (MenuFlyout)FlyoutBase.GetAttachedFlyout(plus)!;
                menu.Items.OfType<MenuItem>().Single(item => item.DataContext is TimelineValueRow row && row.Property == "flipbook.index").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Hide(); TimelinePump(window);
                Assert.That(timeline.Source.Parameters["timeline"].GetProperty("tracks")[0].GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(2.5f));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); history.Clear(); TimelinePump(window);
                var row = session.Rows.Single(item => item.HasTrack); var input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => ReferenceEquals(control.DataContext, row));
                var text = input.GetVisualDescendants().OfType<TextBox>().Single(); text.Focus(); text.Text = "7.25"; TimelinePump(window); PressGroupKey(text, Key.Enter); TimelinePump(window);
                var saved = timeline.Source.Parameters["timeline"].Clone(); var keys = saved.GetProperty("tracks")[0].GetProperty("keys");
                Assert.That(keys[0].GetProperty("value").GetSingle(), Is.EqualTo(2.5f)); Assert.That(keys[1].GetProperty("value").GetSingle(), Is.EqualTo(7.25f), "Use raw float indices, even beyond FrameCount.");
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); TimelinePump(window);
                var stale = session.Rows.Single(item => item.HasTrack); var parameters = layer.Source.Parameters.Where(pair => pair.Key != "flipbook").ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
                scope.GetRequiredService<EntryFieldAuthoringService>().SetParameters(group.Id, layer.Source, layer.Type, parameters); TimelinePump(window);
                stale.DraftValue = 3; Assert.That(stale.Commit(), Is.False);
                Assert.That(JsonElement.DeepEquals(timeline.Source.Parameters["timeline"], saved), Is.True, "Removing the source capability never deletes an authored track.");
                await history.UndoAsync(); TimelinePump(window);
                var localization = scope.GetRequiredService<IEditorLocalizationService>();
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    localization.ApplyLocale(locale); TimelinePump(window);
                    Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsEffectivelyVisible && block.Text == localization["Timeline.Property.flipbook.index"]), Is.True);
                }
                await workspace.SaveAsync(); var path = project.RootPath; var groupId = group.Id; var layerId = layer.StableId; var timelineId = timeline.StableId;
                window.Close(); page.Dispose(); await projects.CloseAsync(); project = await projects.OpenAsync(path);
                var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId);
                Assert.That(reopened.Entries.Single(item => item.StableId == layerId).Source.Parameters["flipbook"].GetProperty("future")[0].GetInt64(), Is.EqualTo(9007199254740993L));
                Assert.That(JsonElement.DeepEquals(reopened.Entries.Single(item => item.StableId == timelineId).Source.Parameters["timeline"], saved), Is.True);
                Assert.That(project.Services.GetRequiredService<ProjectSceneTargets>().Before(groupId, timelineId).Targets.Single(target => target.Handle == handle).Values["flipbook.index"], Is.EqualTo(2.5f));
                await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
