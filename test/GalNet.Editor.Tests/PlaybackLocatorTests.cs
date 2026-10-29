using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task PlaybackLocatorUsesRealSharedPickerFourLocalesHistoryStaleLeasesAndReopen()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "PlaybackLocator"), "Playback", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var layer = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single();
            var animate = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.Animate).Single(); var animateId = animate.Source.Parameters["playbackHandleId"].GetString()!;
            animate.Source.Parameters["handleId"] = layer.Source.Parameters["handleId"].Clone(); animate.Source.Parameters["loopMode"] = JsonSerializer.SerializeToElement("Loop");
            var timeline = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.PlayTimeline).Single(); var timelineId = Guid.NewGuid().ToString("N");
            timeline.Source.Parameters["timeline"] = JsonSerializer.SerializeToElement(new { playbackHandleId = timelineId, durationFrames = 20, loopMode = "Loop", events = new object[0], tracks = new object[0] });
            var stop = workspace.InsertEntriesInto(group, 3, 1, BuiltinEntryIds.StopAnimation).Single();
            stop.Source.Parameters["playbackHandleId"] = JsonSerializer.SerializeToElement("missing-reference");
            stop.Source.Parameters["future"] = JsonSerializer.Deserialize<JsonElement>("{\"number\":9007199254740993,\"values\":[null,true]}");
            layer.IsParametersExpanded = animate.IsParametersExpanded = timeline.IsParametersExpanded = false;
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = stop;
            var entries = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = entries, Width = 1200, Height = 950 }; window.Show(); TimelinePump(window);
            var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear(); var text = scope.GetRequiredService<IEditorLocalizationService>();
            var catalog = scope.GetRequiredService<SceneTargetCatalog>();
            SceneTargetSnapshot Snapshot() => ((SceneFieldChoice)catalog.FieldOptions(group.Id, stop.StableId, stop.Type, "playbackHandleId")![0].Label!).Snapshot;
            try
            {
                Button StopButton() => entries.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "SceneTargetField"
                    && control.GetVisualAncestors().OfType<Control>().Any(parent => ReferenceEquals(parent.DataContext, stop)));
                (Flyout Flyout, SceneTargetPicker Picker) OpenPicker()
                {
                    var currentButton = StopButton(); currentButton.BringIntoView(); TimelinePump(window); TimelineClick(window, currentButton);
                    var opened = (Flyout)currentButton.Flyout!;
                    Assert.That(opened.IsOpen, Is.True);
                    Assert.That(opened.Content, Is.TypeOf<SceneTargetPicker>());
                    return (opened, (SceneTargetPicker)opened.Content!);
                }
                var button = StopButton();
                Assert.That(button.Content, Is.EqualTo(text["Scene.Unresolved"]));
                var snapshot = Snapshot(); Assert.That(snapshot.Playback, Is.True); Assert.That(snapshot.Choices, Has.Count.EqualTo(2));
                Assert.That(snapshot.Choices.Select(choice => choice.Target.Handle), Is.EquivalentTo(new[] { animateId, timelineId }));
                Assert.That(snapshot.Choices.All(choice => choice.Target.Kind == SceneTargetKind.Playback && !choice.Item.Label.Contains(choice.Target.Handle) && !choice.Item.Detail.Contains(choice.Target.Handle)), Is.True);
                Assert.That(catalog.Query(group.Id, stop.StableId).Choices.Select(choice => choice.Target.Kind), Is.EqualTo(new[] { SceneTargetKind.Layer }));
                using var lease = new SceneTargetSelection(catalog, snapshot); Assert.That(lease.Select(snapshot.Choices.Single(choice => choice.Target.Handle == timelineId).Item.Id), Is.True);
                // Refreshing labels must preserve the playback domain and selection lease.
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    text.ApplyLocale(locale); TimelinePump(window); lease.RefreshLabels();
                    Assert.That(lease.Snapshot.Playback, Is.True); Assert.That(lease.TryResolve(out var resolved), Is.True); Assert.That(resolved, Is.EqualTo(timelineId));
                    Assert.That(lease.Items.Any(item => item.Label.Contains(text["Entry.Type.timeline.play"])), Is.True);
                    Assert.That(entries.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsEffectivelyVisible && block.Text == text["Scene.Playback"]), Is.True);
                }
                var (flyout, picker) = OpenPicker();
                Assert.That(picker.ResourcePanel.Search.PlaceholderText, Is.EqualTo(text["Scene.SearchPlayback"]));
                picker.ResourcePanel.Search.Text = "unmatched-playback"; TimelinePump(window); PressGroupKey(picker.ResourcePanel.Search, Key.Down); PressGroupKey(picker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                Assert.That(stop.Source.Parameters["playbackHandleId"].GetString(), Is.EqualTo("missing-reference")); Assert.That(history.CanUndo, Is.False);
                picker.ResourcePanel.Search.Text = BuiltinEntryIds.PlayTimeline; TimelinePump(window); PressGroupKey(picker.ResourcePanel.Search, Key.Down); PressGroupKey(picker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                Assert.That(stop.Source.Parameters["playbackHandleId"].GetString(), Is.EqualTo(timelineId));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(stop.Source.Parameters["playbackHandleId"].GetString(), Is.EqualTo("missing-reference")); await history.RedoAsync(); TimelinePump(window);
                Assert.That(stop.Source.Parameters["future"].GetProperty("number").GetInt64(), Is.EqualTo(9007199254740993L));
                // Choosing an empty value is explicit and reversible.
                history.Clear(); (flyout, picker) = OpenPicker();
                picker.ResourcePanel.Search.Text = text["Scene.None"]; TimelinePump(window); PressGroupKey(picker.ResourcePanel.Search, Key.Down); PressGroupKey(picker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                Assert.That(stop.Source.Parameters["playbackHandleId"].GetString(), Is.Empty); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                // A live popup cannot write after its source author snapshot changes.
                (flyout, picker) = OpenPicker(); var stalePicker = picker;
                var author = scope.GetRequiredService<EntryFieldAuthoringService>(); author.SetCondition(group.Id, animate.Source, "false");
                Assert.That(lease.TryResolve(out _), Is.False); var before = JsonSerializer.Serialize(stop.Source.Parameters);
                stalePicker.ResourcePanel.Search.Text = text["Entry.Type.animation.animate"]; TimelinePump(window); PressGroupKey(stalePicker.ResourcePanel.Search, Key.Down); PressGroupKey(stalePicker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                Assert.That(JsonSerializer.Serialize(stop.Source.Parameters), Is.EqualTo(before)); flyout.Hide();
                await history.UndoAsync(); TimelinePump(window); Assert.That(lease.TryResolve(out _), Is.False, "Undo never renews an old selection lease.");
                using var current = new SceneTargetSelection(catalog, Snapshot()); Assert.That(current.Select(current.Items.Single(item => item.Label.Contains(text["Entry.Type.timeline.play"])).Id), Is.True);
                await workspace.SaveAsync(); Assert.That(current.TryResolve(out _), Is.True);
                page.SelectedEntry = timeline;
                var session = scope.GetRequiredService<TimelineEditSession>(); var draft = session.BeginObjectEdit()!;
                Assert.That(draft.Snapshot.Playback, Is.False); Assert.That(draft.Choices, Has.Count.EqualTo(1));
                Assert.That(draft.Snapshot.Choices.Single().Target.Kind, Is.EqualTo(SceneTargetKind.Layer)); session.EndObjectEdit();
                var path = project.RootPath; var groupId = group.Id; var stopId = stop.StableId;
                window.Close(); page.Dispose(); await projects.CloseAsync(); Assert.That(current.TryResolve(out _), Is.False);
                project = await projects.OpenAsync(path); scope = project.Services;
                var reopened = scope.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(entry => entry.StableId == stopId);
                Assert.That(reopened.Source.Parameters["playbackHandleId"].GetString(), Is.EqualTo(timelineId)); Assert.That(reopened.Source.Parameters["future"].GetProperty("number").GetInt64(), Is.EqualTo(9007199254740993L));
                var options = scope.GetRequiredService<SceneTargetCatalog>().FieldOptions(groupId, stopId, BuiltinEntryIds.StopAnimation, "playbackHandleId")!;
                Assert.That(options.Any(option => option.Value.GetString() == timelineId), Is.True);
                await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
