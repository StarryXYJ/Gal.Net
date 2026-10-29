using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Core;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private static JsonElement TimelineFixture() => JsonSerializer.Deserialize<JsonElement>("""
        { "FrameRate":60, "DurationFrames":10, "opaque":{"number":9007199254740993},
          "Tracks":[{"HandleId":"hidden-target","Property":"transform.x","BlendMode":"Replace","extra":"keep",
            "Keys":[{"Frame":0,"Value":2,"InTangent":0.1,"OutTangent":0.4,"InterpolationToNext":"CubicHermite","future":[1,null]},
                    {"Frame":10,"Value":8,"InTangent":-0.2,"OutTangent":0.7}]}],"Events":[] }
        """);
    private static IEnumerable<IDockable> TimelineDockables(IDockable item)
    {
        yield return item;
        if (item is not IDock { VisibleDockables: { } children }) yield break;
        foreach (var child in children) foreach (var nested in TimelineDockables(child)) yield return nested;
    }
    private static void TimelinePump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    [Test]
    public async Task TimelineDockOpensOnDemandFollowsActiveEntryAndNumericEditsRoundTripThroughHistoryAndDisk()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TimelineDock"), "Timeline", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single();
            entry.Source.Parameters = new() { ["timeline"] = TimelineFixture(), ["future"] = JsonSerializer.SerializeToElement(new[] { 1, 2 }) };
            var ordinary = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.Wait).Single();
            var factory = project.Services.GetRequiredService<EditorDockFactory>(); var layout = factory.CreateLayout(); factory.InitLayout(layout);
            Assert.That(TimelineDockables(layout).Any(item => item.Id == EditorDockPanelIds.Timeline), Is.False);
            factory.OpenPanel(EditorDockPanelIds.Timeline);
            var panels = TimelineDockables(layout).ToArray(); var dock = panels.Single(item => item.Id == EditorDockPanelIds.Timeline);
            Assert.That(dock.Owner, Is.SameAs(panels.Single(item => item.Id == EditorDockPanelIds.Assets).Owner));
            factory.OpenGroupEditor(group);
            var page = TimelineDockables(layout).Select(item => item.Context).OfType<GroupEditorPanelViewModel>().Single();
            var session = project.Services.GetRequiredService<TimelineEditSession>();
            var timelineView = (TimelinePanelView)project.Services.GetRequiredService<GalNet.Editor.Services.Interfaces.IEditorViewFactory>().CreateViewForViewModel(dock.Context!)!;
            timelineView.Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Themes/EditorButtons.axaml") });
            var groupView = new EntryListEditorControl { DataContext = page };
            var container = new Grid { RowDefinitions = new RowDefinitions("*,*") }; container.Children.Add(groupView); container.Children.Add(timelineView); Grid.SetRow(timelineView, 1);
            var window = new Window { Content = container, Width = 1250, Height = 950 }; window.Show(); TimelinePump(window);
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                Assert.That(session.StatusKey, Is.EqualTo("Timeline.SelectEntry")); page.SelectedEntry = entry; TimelinePump(window);
                Assert.That(session.Rows, Has.Count.EqualTo(1)); Assert.That(history.CanUndo, Is.False);
                var objectsSection = timelineView.FindControl<Grid>("TimelineObjectsSection")!;
                var eventsSection = timelineView.FindControl<Grid>("TimelineEventsSection")!;
                Assert.That(objectsSection.Bounds.Bottom, Is.LessThanOrEqualTo(eventsSection.Bounds.Top));
                Assert.That(objectsSection.Bounds.Height, Is.EqualTo(eventsSection.Bounds.Height).Within(1));
                var splitter = timelineView.FindControl<GridSplitter>("TimelineSectionsSplitter")!;
                var originalObjectsHeight = objectsSection.Bounds.Height;
                var originalEventsHeight = eventsSection.Bounds.Height;
                var splitterPoint = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
                window.MouseDown(splitterPoint, MouseButton.Left); window.MouseMove(splitterPoint + new Vector(0, 24)); window.MouseUp(splitterPoint + new Vector(0, 24), MouseButton.Left); TimelinePump(window);
                Assert.That(objectsSection.Bounds.Height, Is.EqualTo(originalObjectsHeight + 24).Within(1));
                Assert.That(eventsSection.Bounds.Height, Is.EqualTo(originalEventsHeight - 24).Within(1));
                Assert.That(history.CanUndo, Is.False, "Changing the panel split is not an author edit.");
                Assert.That(timelineView.GetVisualDescendants().OfType<Button>().Any(button => button.Content is string text && new[] { session.SceneText["Uiux.Timeline.Playhead"], session.SceneText["Timeline.ReplaceCollisions"], session.SceneText["Timeline.CancelMove"] }.Contains(text)), Is.False);

                Assert.That(timelineView.FindControl<NumericUpDown>("TimelineFrame"), Is.Null);
                Assert.That(timelineView.FindControl<TextBlock>("TimelineStatus")!.IsEffectivelyVisible, Is.False);
                var zoom = timelineView.FindControl<Slider>("TimelineZoom")!;
                var currentFrame = timelineView.FindControl<TextBlock>("TimelineCurrentFrame")!;
                Assert.That(currentFrame.TranslatePoint(default, timelineView)!.Value.X + currentFrame.Bounds.Width, Is.LessThan(zoom.TranslatePoint(default, timelineView)!.Value.X));
                Assert.That(zoom.TranslatePoint(default, timelineView)!.Value.X + zoom.Bounds.Width, Is.EqualTo(timelineView.Bounds.Width - 8).Within(1));
                var durationInput = timelineView.FindControl<NumericUpDown>("TimelineDurationDraft")!;
                var fpsInput = timelineView.FindControl<NumericUpDown>("TimelineFpsDraft")!;
                Assert.That(durationInput.TranslatePoint(default, timelineView)!.Value.Y, Is.EqualTo(fpsInput.TranslatePoint(default, timelineView)!.Value.Y).Within(1));
                foreach (var name in new[] { "TimelinePreviousKey", "TimelineNextKey", "TimelineAddKey", "TimelineDeleteKeys", "AddTimelineObject" })
                {
                    var button = timelineView.FindControl<Button>(name)!;
                    Assert.That(button.Content, Is.TypeOf<PathIcon>());
                    Assert.That(button.Background, Is.EqualTo(Avalonia.Media.Brushes.Transparent));
                }
                var input = timelineView.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.Name == "TimelineValue");
                Assert.That(input.GetVisualDescendants().OfType<TextBox>().Any(), Is.True, "The actual themed numeric editor must be mounted.");
                Assert.That(input.Value, Is.EqualTo(2)); Assert.That(timelineView.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text?.Contains("hidden-target", StringComparison.Ordinal) == true), Is.False);
                input.Value = 3; TimelinePump(window); PressGroupKey(input, Key.Enter); TimelinePump(window);
                var root = entry.Source.Parameters["timeline"]; var key = root.GetProperty("Tracks")[0].GetProperty("Keys")[0];
                Assert.That(key.GetProperty("Value").GetSingle(), Is.EqualTo(3)); Assert.That(key.GetProperty("OutTangent").GetSingle(), Is.EqualTo(.4f)); Assert.That(key.GetProperty("future")[1].ValueKind, Is.EqualTo(JsonValueKind.Null));
                var old = session.Rows.Single(); session.Frame = 5; TimelinePump(window); Assert.That(currentFrame.Text, Is.EqualTo(session.SceneText.Format("Timeline.CurrentFrame", 5))); old.DraftValue = 99; Assert.That(old.Commit(), Is.False);
                input = timelineView.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.Name == "TimelineValue"); Assert.That(input.Value, Is.EqualTo(6.25m));
                input.Value = 6.4m; input.Value = 6.5m; TimelinePump(window); PressGroupKey(input, Key.Enter); TimelinePump(window);
                var keys = entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys");
                Assert.That(keys.GetArrayLength(), Is.EqualTo(3)); Assert.That(keys[1].GetProperty("frame").GetInt32(), Is.EqualTo(5));
                Assert.That(keys[1].GetProperty("value").GetSingle(), Is.EqualTo(6.5f)); Assert.That(keys[1].GetProperty("outTangent").GetSingle(), Is.EqualTo(.7f).Within(.0001));
                Assert.That(keys[1].GetProperty("interpolationToNext").GetString(), Is.EqualTo("CubicHermite"));
                Assert.That(entry.Source.Parameters["timeline"].GetProperty("opaque").GetProperty("number").GetRawText(), Is.EqualTo("9007199254740993"));
                await history.UndoAsync(); Assert.That(entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys").GetArrayLength(), Is.EqualTo(2));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False, "Each complete numeric gesture is exactly one project history step.");
                await history.RedoAsync(); await history.RedoAsync();
                var row = session.Rows.Single(); row.DraftValue = 9; await workspace.SaveAsync(); Assert.That(session.Rows.Single(), Is.SameAs(row), "Saving an unchanged root must not destroy pending local input."); row.Cancel();
                input = timelineView.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.Name == "TimelineValue"); input.Value = 10; TimelinePump(window); PressGroupKey(input, Key.Escape); Assert.That(session.Rows.Single().DraftValue, Is.EqualTo(6.5m));
                page.SelectedEntry = ordinary; TimelinePump(window); Assert.That(session.StatusKey, Is.EqualTo("Timeline.Unsupported")); Assert.That(session.CanEdit, Is.False);
                row.DraftValue = 50; Assert.That(row.Commit(), Is.False); page.SelectedEntry = null; Assert.That(session.StatusKey, Is.EqualTo("Timeline.SelectEntry"));
                page.SetSelection([entry, ordinary]); Assert.That(session.CanEdit, Is.False, "Mixed selection follows the active ordinary item.");
                page.SetSelection([ordinary, entry]); Assert.That(session.CanEdit, Is.True);
                page.SelectedEntry = entry; TimelinePump(window); factory.OpenPanel(EditorDockPanelIds.Timeline); Assert.That(session.CanEdit, Is.True);
                var localization = project.Services.GetRequiredService<IEditorLocalizationService>();
                var localDraft = session.Rows.Single(); localDraft.DraftValue = 12;
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    localization.ApplyLocale(locale); TimelinePump(window);
                    Assert.That(timelineView.FindControl<TextBlock>("TimelineStatus")!.IsEffectivelyVisible, Is.False);
                    Assert.That(currentFrame.Text, Is.EqualTo(localization.Format("Timeline.CurrentFrame", session.Frame)));
                    Assert.That(session.Rows.Single().ObjectLabel, Is.EqualTo(localization.Format("Timeline.Object", 1)));
                    Assert.That(session.Rows.Single(), Is.SameAs(localDraft)); Assert.That(localDraft.DraftValue, Is.EqualTo(12));
                }
                localDraft.Cancel();
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "p6d-timeline-dock.png")); }
                var stale = session.Rows.Single(); factory.ToggleGlobalPanel(EditorDockPanelIds.Timeline); stale.DraftValue = 44; Assert.That(stale.Commit(), Is.False);
                factory.OpenPanel(EditorDockPanelIds.Timeline); Assert.That(TimelineDockables(layout).Count(item => item.Id == EditorDockPanelIds.Timeline), Is.EqualTo(1));
                await workspace.SaveAsync(); var path = project.RootPath; var groupId = group.Id; var id = entry.StableId;
                window.Close(); page.Dispose(); await projects.CloseAsync(); stale.DraftValue = 33; Assert.That(stale.Commit(), Is.False);
                project = await projects.OpenAsync(path); var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == id);
                Assert.That(reopened.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys")[1].GetProperty("value").GetSingle(), Is.EqualTo(6.5f)); Assert.That(reopened.Source.Parameters["future"].GetArrayLength(), Is.EqualTo(2));
                await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TimelineSelectionOwnersMissingInvalidAndDeletedEntriesNeverReviveOldRows()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TimelineSelection"), "Selection", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); var otherGroup = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 500, 0);
            var other = workspace.InsertEntriesInto(otherGroup, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); other.Source.Parameters = new() { ["timeline"] = TimelineFixture() };
            using var first = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); using var second = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, otherGroup.Id);
            var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            first.SelectedEntry = entry; Assert.That(session.CanCreate, Is.True); Assert.That(history.CanUndo, Is.False); session.Create();
            Assert.That(session.HasNoRows, Is.True); Assert.That(entry.Source.Parameters["timeline"].GetProperty("playbackHandleId").GetString(), Is.Not.Empty);
            await history.UndoAsync(); Assert.That(session.CanCreate, Is.True); Assert.That(history.CanUndo, Is.False); await history.RedoAsync();
            second.SelectedEntry = other; Assert.That(session.Rows, Has.Count.EqualTo(1)); first.Dispose(); Assert.That(session.Rows, Has.Count.EqualTo(1), "Closing an inactive Group must not clear another Group's selection.");
            var old = session.Rows.Single(); scope.GetRequiredService<GroupEntryAuthoringService>().Delete(otherGroup.Id, [other.StableId]);
            Assert.That(session.CanEdit, Is.False); await history.UndoAsync(); old.DraftValue = 9; Assert.That(old.Commit(), Is.False);
            second.SelectedEntry = other; var row = session.Rows.Single(); second.ChangeType(other, BuiltinEntryIds.Wait); Assert.That(session.StatusKey, Is.EqualTo("Timeline.Unsupported")); await history.UndoAsync(); row.DraftValue = 7; Assert.That(row.Commit(), Is.False);
            scope.GetRequiredService<EntryFieldAuthoringService>().SetParameters(otherGroup.Id, other.Source, other.Type, new() { ["timeline"] = JsonSerializer.SerializeToElement("invalid") });
            Assert.That(session.StatusKey, Is.EqualTo("Timeline.Invalid")); Assert.That(session.CanCreate, Is.False); Assert.That(other.Source.Parameters["timeline"].GetString(), Is.EqualTo("invalid"));
            second.SelectedEntry = null; Assert.That(session.StatusKey, Is.EqualTo("Timeline.SelectEntry")); second.Dispose(); await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [TestCase("null", "Timeline.Missing")]
    [TestCase("{}", "Timeline.Editing")]
    [TestCase("{\"tracks\":null}", "Timeline.Invalid")]
    [TestCase("{\"events\":null}", "Timeline.Invalid")]
    [TestCase("{\"frameRate\":null}", "Timeline.Invalid")]
    [TestCase("{\"durationFrames\":-1}", "Timeline.Invalid")]
    [TestCase("\"invalid\"", "Timeline.Invalid")]
    public async Task TimelineSelectionNeverNormalizesOrRewritesIncompleteOrInvalidAuthorJson(string json, string status)
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TimelineInvalid"), "Invalid", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); entry.Source.Parameters = new() { ["timeline"] = JsonSerializer.Deserialize<JsonElement>(json) };
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); var session = scope.GetRequiredService<TimelineEditSession>();
            var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear(); page.SelectedEntry = entry;
            Assert.That(session.StatusKey, Is.EqualTo(status)); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(json));
            await workspace.SaveAsync(); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(json));
            page.Dispose(); await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }
}
