using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task SceneCreatorsPastedWithTimelineUseNewInternalIdsAndRedoRetainsThoseIds()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "SceneCopy"), "Copy", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
        var creator = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single(); var id = creator.Source.Parameters["handleId"].GetString();
        var timeline = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.PlayTimeline).Single();
        timeline.Source.Parameters["timeline"] = JsonSerializer.SerializeToElement(new { playbackHandleId = "play", tracks = new[] { new { handleId = id, property = "opacity", keys = new[] { new { frame = 0, value = 1 } } } }, events = Array.Empty<object>() });
        var author = scope.GetRequiredService<GroupEntryAuthoringService>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        var copy = (await author.PasteAsync(group.Id, author.Copy(group.Id, [creator.StableId, timeline.StableId]), timeline.StableId)).Select(id => group.Entries.Single(entry => entry.StableId == id)).ToArray();
        var copiedId = copy[0].Source.Parameters["handleId"].GetString(); Assert.That(copiedId, Is.Not.EqualTo(id)); Assert.That(copy[1].Source.Parameters["timeline"].GetProperty("tracks")[0].GetProperty("handleId").GetString(), Is.EqualTo(copiedId));
        await history.UndoAsync(); Assert.That(group.Entries, Has.Count.EqualTo(2)); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); Assert.That(copy[0].Source.Parameters["handleId"].GetString(), Is.EqualTo(copiedId));
        history.Clear(); var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.ChangeType(creator, BuiltinEntryIds.PlayParticleEmitter); var particleId = creator.Source.Parameters["instanceId"].GetString(); Assert.That(Guid.TryParse(particleId, out _), Is.True);
        await history.UndoAsync(); Assert.That(creator.Source.Parameters["handleId"].GetString(), Is.EqualTo(id)); await history.RedoAsync(); Assert.That(creator.Source.Parameters["instanceId"].GetString(), Is.EqualTo(particleId));
        page.Dispose(); await projects.CloseAsync();
    }
    private static void TimelineClick(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); TimelinePump(window);
    }
    private static MenuFlyout TimelineOverrideMenu(Window window, TimelinePanelView view, TimelineTreeNode node)
    {
        var row = view.GetVisualDescendants().OfType<Grid>().Single(control => control.Name == "TimelineTreeRow" && ReferenceEquals(control.DataContext, node));
        var point = row.TranslatePoint(new Point(Math.Min(100, row.Bounds.Width / 4), row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); TimelinePump(window);
        var menu = (MenuFlyout)FlyoutBase.GetAttachedFlyout(row)!;
        Assert.That(menu.IsOpen, Is.True);
        if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
        { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "timeline-ux-override-menu.png")); }
        return menu;
    }
    private static void RemoveTimelineOverride(Window window, TimelinePanelView view, TimelineTreeNode node)
    {
        var menu = TimelineOverrideMenu(window, view, node);
        menu.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Hide(); TimelinePump(window);
    }
    [Test]
    public async Task TimelinePropertyTreeCreatesTracksThroughRealInputsAndRetainsIdentityHistoryAndUnknownJson()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TimelineTree"), "Tree", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
            var layers = workspace.InsertEntriesInto(group, 0, 2, BuiltinEntryIds.ShowLayer); var handles = layers.Select(entry => entry.Source.Parameters["handleId"].GetString()).ToArray();
            Assert.That(handles.All(value => Guid.TryParse(value, out _)), Is.True); Assert.That(handles.Distinct().Count(), Is.EqualTo(2));
            var timeline = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.PlayTimeline).Single();
            timeline.Source.Parameters = new() { ["timeline"] = JsonSerializer.Deserialize<JsonElement>("""{"playbackHandleId":"play","durationFrames":60,"future":[9007199254740993,null],"tracks":[],"events":[]}""") };
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = timeline;
            var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var view = scope.GetRequiredService<TimelinePanelView>(); view.DataContext = scope.GetRequiredService<TimelinePanelViewModel>();
            var window = new Window { Content = view, Width = 1350, Height = 950 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(session.Tree, Has.Count.EqualTo(2)); Assert.That(session.AnalysisIncomplete, Is.False); Assert.That(history.CanUndo, Is.False);
                Assert.That(session.Tree.Select(node => node.Label).Distinct().Count(), Is.EqualTo(2));
                Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(block => handles.Any(handle => block.Text?.Contains(handle!, StringComparison.Ordinal) == true)), Is.False);
                var objectDraft = session.BeginObjectEdit()!;
                objectDraft.Select(objectDraft.Snapshot.Choices.Single(choice => choice.Target.Handle == handles[0]).Item.Id);
                Assert.That(objectDraft.Commit(), Is.True); TimelinePump(window);
                var tree = view.FindControl<ItemsControl>("TimelinePropertyTree")!;
                var expander = tree.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "TimelineExpand" && ReferenceEquals(button.DataContext, session.Tree.Single(node => node.ObjectHandle == handles[0])));
                TimelineClick(window, expander); Assert.That(session.Tree.Single(node => node.ObjectHandle == handles[0]).IsExpanded, Is.False); session.Frame = 5; TimelinePump(window); Assert.That(session.Tree.Single(node => node.ObjectHandle == handles[0]).IsExpanded, Is.False, "Collapsed objects survive playback-head refresh.");
                TimelineClick(window, tree.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "TimelineExpand" && ReferenceEquals(button.DataContext, session.Tree.Single(node => node.ObjectHandle == handles[0]))));
                var row = session.Tree.Single(node => node.ObjectHandle == handles[0]).Children.SelectMany(node => node.Children).Select(node => node.Value).Single(item => item?.Property == "transform.x")!;
                Assert.That(session.AddProperty(row), Is.True); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); history.Clear(); TimelinePump(window);
                row = session.Rows.Single(item => item.HasTrack);
                var input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => ReferenceEquals(control.DataContext, row));
                var text = input.GetVisualDescendants().OfType<TextBox>().Single(); text.Focus(); text.Text = "15"; TimelinePump(window); PressGroupKey(text, Key.Enter); TimelinePump(window);
                var root = timeline.Source.Parameters["timeline"]; var keys = root.GetProperty("tracks")[0].GetProperty("keys");
                Assert.That(keys.GetArrayLength(), Is.EqualTo(2)); Assert.That(keys[0].GetProperty("frame").GetInt32(), Is.EqualTo(0)); Assert.That(keys[0].GetProperty("value").GetSingle(), Is.EqualTo(0)); Assert.That(keys[1].GetProperty("value").GetSingle(), Is.EqualTo(15));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(timeline.Source.Parameters["timeline"].GetProperty("tracks")[0].GetProperty("keys")[1].GetProperty("value").GetSingle(), Is.EqualTo(0)); await history.RedoAsync(); TimelinePump(window);
                row = session.Rows.Single(item => item.Property == "transform.x" && item.HasTrack);
                RemoveTimelineOverride(window, view, session.VisibleNodes.Single(node => ReferenceEquals(node.Value, row)));
                Assert.That(timeline.Source.Parameters["timeline"].GetProperty("tracks").GetArrayLength(), Is.EqualTo(0)); await history.UndoAsync(); TimelinePump(window);
                var stale = session.Rows.Single(item => item.HasTrack); var author = scope.GetRequiredService<EntryFieldAuthoringService>();
                var trackedHandle = timeline.Source.Parameters["timeline"].GetProperty("tracks")[0].GetProperty("handleId").GetString();
                var producer = layers.Single(item => item.Source.Parameters["handleId"].GetString() == trackedHandle);
                author.SetCondition(group.Id, producer.Source, "save.flag"); TimelinePump(window);
                Assert.That(session.Tree.Single(node => node.ObjectHandle == handles[0]).Label, Does.Contain(scope.GetRequiredService<IEditorLocalizationService>()["Timeline.Possible"])); stale.DraftValue = 44; Assert.That(stale.Commit(), Is.False);
                var unknown = session.Tree.Single(node => node.ObjectHandle == handles[0]).Children.Single(node => node.Value?.Property == "opacity").Value!;
                Assert.That(unknown.DraftValue, Is.Null);
                Assert.That(session.AddProperty(unknown), Is.True); TimelinePump(window);
                Assert.That(session.VisibleNodes.Any(node => ReferenceEquals(node.Value, unknown)), Is.True, "Unknown initial values remain a visible draft until entered.");
                unknown.DraftValue = 1.2m; Assert.That(unknown.Commit(), Is.False, "Replace enforces the declared property range.");
                unknown.DraftValue = .25m; Assert.That(unknown.Commit(), Is.True); var created = timeline.Source.Parameters["timeline"].GetProperty("tracks")[1];
                Assert.That(created.GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(.25f), "An unknown baseline uses the explicit author value at frame zero.");
                await history.UndoAsync(); unknown = session.Tree.Single(node => node.ObjectHandle == handles[0]).Children.Single(node => node.Value?.Property == "opacity").Value!;
                unknown.IsAdditive = true; unknown.DraftValue = 2; Assert.That(unknown.Commit(), Is.True); created = timeline.Source.Parameters["timeline"].GetProperty("tracks")[1];
                Assert.That(created.GetProperty("blendMode").GetString(), Is.EqualTo("Additive")); Assert.That(created.GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(0)); Assert.That(created.GetProperty("keys")[1].GetProperty("value").GetSingle(), Is.EqualTo(2));
                TimelinePump(window); var opacityMode = view.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "TimelineBlendMode" && control.DataContext is TimelineValueRow item && item.HasTrack && item.Property == "opacity");
                Assert.That(opacityMode.SelectedIndex, Is.EqualTo(1)); history.Clear(); opacityMode.Focus(); PressGroupKey(opacityMode, Key.Up); TimelinePump(window);
                Assert.That(opacityMode.SelectedIndex, Is.EqualTo(1), "A rejected absolute mode must still display the actual additive mode."); Assert.That(history.CanUndo, Is.False);
                Assert.That(timeline.Source.Parameters["timeline"].GetProperty("tracks")[1].GetProperty("blendMode").GetString(), Is.EqualTo("Additive"));
                // Remove the additive track explicitly so the remaining save assertion keeps its original scope.
                unknown = session.Rows.Single(item => item.HasTrack && item.Property == "opacity"); Assert.That(session.RemoveOverride(session.VisibleNodes.Single(node => ReferenceEquals(node.Value, unknown))), Is.True);
                var draft = session.Rows.First(item => !item.HasTrack && item.Property == "opacity"); draft.DraftValue = .5m;
                var localization = scope.GetRequiredService<IEditorLocalizationService>(); foreach (var locale in new[] { "ja-JP", "ko-KR", "en-US", "zh-CN" }) { localization.ApplyLocale(locale); TimelinePump(window); Assert.That(session.Tree.Single(node => node.ObjectHandle == handles[0]).Label, Does.Contain(localization["Timeline.Possible"])); Assert.That(draft.DraftValue, Is.EqualTo(.5m)); }
                draft.Cancel();
                var path = project.RootPath; var groupId = group.Id; var entryId = timeline.StableId;
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "p6e1-property-tree.png")); }
                await workspace.SaveAsync(); window.Close(); page.Dispose(); await projects.CloseAsync(); project = await projects.OpenAsync(path);
                var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == entryId).Source.Parameters["timeline"];
                Assert.That(reopened.GetProperty("future")[0].GetRawText(), Is.EqualTo("9007199254740993")); Assert.That(reopened.GetProperty("tracks")[0].GetProperty("keys")[1].GetProperty("value").GetSingle(), Is.EqualTo(15)); await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
