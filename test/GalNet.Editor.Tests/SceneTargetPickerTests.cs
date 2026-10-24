using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
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
    public async Task AssetPickerUsesSharedCardsForSearchKeyboardSelectionAndEmptyValue()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(() =>
        {
            PrepareGroupTheme(); using var assets = new PickerAssets();
            var picker = new AssetPicker { AssetManager = assets, Filter = GalNet.Editor.Models.AssetPickerFilter.Audio };
            var window = new Window { Content = picker, Width = 650, Height = 600 }; window.Show(); TimelinePump(window);
            try
            {
                var button = picker.FindControl<Button>("PickerButton")!; TimelineClick(window, button);
                Assert.That(picker.ResourcePanel.Results.Select(item => item.Id), Is.EquivalentTo(new[] { "", "voice" }));
                picker.ResourcePanel.Search.Text = "voice.ogg"; TimelinePump(window);
                Assert.That(picker.ResourcePanel.Results.Single().Detail, Is.EqualTo("Audio/voice.ogg"));
                PressGroupKey(picker.ResourcePanel.Search, Key.Down); PressGroupKey(picker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                Assert.That(picker.SelectedAssetId, Is.EqualTo("voice")); Assert.That(picker.DisplayText, Is.EqualTo("voice.ogg"));
                TimelineClick(window, button); picker.ResourcePanel.Search.Text = ""; TimelinePump(window);
                PressGroupKey(picker.ResourcePanel.Search, Key.Up); PressGroupKey(picker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                Assert.That(picker.SelectedAssetId, Is.Empty);
                TimelineClick(window, button); PressGroupKey(picker.ResourcePanel.Search, Key.Escape); Assert.That(((Flyout)button.Flyout!).IsOpen, Is.False);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class PickerAssets : IAssetManager
    {
        private readonly IGameFile[] _files = [new PickerFile("voice", "Audio/voice.ogg", "audio"), new PickerFile("movie", "Movies/intro.mp4", "video")];
        public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default) => Task.FromResult(_files.FirstOrDefault(file => file.Id == assetId));
        public Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IGameFile>>(_files.Where(file => typeId is null || file.TypeId == typeId).ToArray());
        public Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class => throw new NotSupportedException();
        public void RegisterProvider(IAssetProvider provider) => throw new NotSupportedException();
        public void Dispose() { }
    }
    private sealed record PickerFile(string Id, string Path, string TypeId) : IGameFile
    {
        public AssetMeta Metadata => new() { Id = Id, Path = Path, TypeId = TypeId };
        public long Length => 0;
        public string? Hash => null;
        public Stream OpenRead() => new MemoryStream();
        public byte[] ReadAllBytes() => [];
        public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
    }

    [Test]
    public async Task SceneQueryIsPositionBasedAndSelectionsRejectChangedOrClosedProjects()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "SceneQuery"), "Query", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
        var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var layers = workspace.InsertEntriesInto(group, 0, 2, BuiltinEntryIds.ShowLayer);
        foreach (var layer in layers) layer.Source.Parameters["assetId"] = JsonSerializer.SerializeToElement("same.png");
        var consumer = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.HideLayer).Single();
        var author = scope.GetRequiredService<EntryFieldAuthoringService>(); author.SetCondition(group.Id, layers[1].Source, "save.optional");
        var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        var query = scope.GetRequiredService<ProjectSceneTargets>();
        Assert.That(query.Before(group.Id, layers[0].StableId).Targets, Is.Empty);
        Assert.That(query.Before(group.Id, layers[1].StableId).Targets, Has.Count.EqualTo(1));
        var state = query.Before(group.Id, consumer.StableId); var revision = query.Revision;
        Assert.That(state.Complete, Is.True); Assert.That(state.Reachable, Is.True); Assert.That(state.Targets, Has.Count.EqualTo(2));
        Assert.That(state.Targets.Select(target => target.Presence), Is.EquivalentTo(new[] { TargetPresence.Definite, TargetPresence.Possible }));
        Assert.That(state.Targets.Select(target => target.Resource).Distinct(), Is.EqualTo(new[] { "same.png" }));
        Assert.That(state.Targets.Select(target => target.Locator).Distinct().Count(), Is.EqualTo(2));
        Assert.That(state.Targets.Single(target => target.Presence == TargetPresence.Possible).Values, Is.Empty);
        Assert.That(query.Before(group.Id, null).Targets, Has.Count.EqualTo(2)); Assert.That(query.Revision, Is.EqualTo(revision));
        Assert.That(query.Before(group.Id, "deleted").Reachable, Is.False);
        var catalog = scope.GetRequiredService<SceneTargetCatalog>();
        using var selection = new SceneTargetSelection(catalog, catalog.Query(group.Id, consumer.StableId));
        await selection.PrepareAsync(); Assert.That(selection.Items.Select(item => item.Label).Distinct().Count(), Is.EqualTo(2));
        var choice = selection.Snapshot.Choices.Single(item => item.Target.Locator.EntryId == layers[0].StableId);
        Assert.That(selection.Items.Any(item => item.Label.Contains(choice.Target.Handle, StringComparison.Ordinal) || item.Detail.Contains(choice.Target.Handle, StringComparison.Ordinal)), Is.False);
        Assert.That(selection.Select(choice.Item.Id), Is.True); Assert.That(selection.TryResolve(out var handle), Is.True); Assert.That(handle, Is.EqualTo(choice.Target.Handle));
        Assert.That(history.CanUndo, Is.False, "Queries and labels never author content.");
        author.SetCondition(group.Id, layers[0].Source, "false"); Assert.That(selection.TryResolve(out _), Is.False);
        Assert.That(query.Revision, Is.GreaterThan(revision)); await history.UndoAsync();
        Assert.That(selection.TryResolve(out _), Is.False, "Undo does not revive a selection from a previous analysis generation.");
        using var current = new SceneTargetSelection(catalog, catalog.Query(group.Id, consumer.StableId)); Assert.That(current.Select(choice.Item.Id), Is.True);
        var staleDraft = consumer.Parameters;
        await workspace.SaveAsync(); Assert.That(current.TryResolve(out _), Is.True, "Saving identical author content retains the generation.");
        Assert.That(consumer.Parameters, Is.EquivalentTo(staleDraft));
        await projects.CloseAsync(); Assert.That(current.TryResolve(out _), Is.False);
        using var closed = new SceneTargetSelection(catalog, catalog.Query(group.Id, consumer.StableId), "Scene.None");
        Assert.That(closed.Select("none"), Is.False);
    }

    [Test]
    public async Task SharedScenePickerSelectsObjectsThenAddsPropertiesFromObjectRowsAndSaves()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "ScenePicker"), "Picker", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var layers = workspace.InsertEntriesInto(group, 0, 2, BuiltinEntryIds.ShowLayer);
            var timeline = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.PlayTimeline).Single();
            timeline.Source.Parameters["timeline"] = JsonSerializer.Deserialize<JsonElement>("""{"playbackHandleId":"play","durationFrames":60,"future":[9007199254740993,null],"tracks":[],"events":[]}""");
            var hide = workspace.InsertEntriesInto(group, 3, 1, BuiltinEntryIds.HideLayer).Single();
            hide.Source.Parameters["handleId"] = JsonSerializer.SerializeToElement("missing-reference");
            hide.Source.Parameters["future"] = JsonSerializer.SerializeToElement(new { retained = true });
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = hide;
            var entries = new EntryListEditorControl { DataContext = page };
            var entryWindow = new Window { Content = entries, Width = 1200, Height = 950 }; entryWindow.Show(); TimelinePump(entryWindow);
            var timelineView = scope.GetRequiredService<TimelinePanelView>(); timelineView.DataContext = scope.GetRequiredService<TimelinePanelViewModel>();
            var timelineWindow = new Window { Content = timelineView, Width = 1350, Height = 950 };
            try
            {
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                var button = entries.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "SceneTargetField");
                Assert.That(button.Content, Is.EqualTo(scope.GetRequiredService<IEditorLocalizationService>()["Scene.Unresolved"]));
                TimelineClick(entryWindow, button); var fieldFlyout = (Flyout)button.Flyout!;
                var picker = (SceneTargetPicker)fieldFlyout.Content!; var catalog = scope.GetRequiredService<SceneTargetCatalog>();
                var first = catalog.Query(group.Id, hide.StableId).Choices.Single(choice => choice.Target.Locator.EntryId == layers[0].StableId);
                picker.ResourcePanel.Search.Text = first.Item.Label; TimelinePump(entryWindow); PressGroupKey(picker.ResourcePanel.Search, Key.Down); PressGroupKey(picker.ResourcePanel.Search, Key.Enter); TimelinePump(entryWindow);
                Assert.That(hide.Source.Parameters["handleId"].GetString(), Is.EqualTo(first.Target.Handle));
                Assert.That(hide.Source.Parameters["future"].GetProperty("retained").GetBoolean(), Is.True);
                var localization = scope.GetRequiredService<IEditorLocalizationService>(); localization.ApplyLocale("en-US"); TimelinePump(entryWindow);
                Assert.That(button.Content?.ToString(), Does.Contain(localization["Timeline.Kind.Layer"]));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(hide.Source.Parameters["handleId"].GetString(), Is.EqualTo("missing-reference")); await history.RedoAsync();
                // An open selector cannot commit after its author context changes.
                TimelineClick(entryWindow, button); var oldPicker = (SceneTargetPicker)fieldFlyout.Content!;
                scope.GetRequiredService<EntryFieldAuthoringService>().SetCondition(group.Id, layers[1].Source, "save.optional");
                var beforeStale = JsonSerializer.Serialize(hide.Source.Parameters);
                PressGroupKey(oldPicker.ResourcePanel.Search, Key.Down); PressGroupKey(oldPicker.ResourcePanel.Search, Key.Enter); TimelinePump(entryWindow);
                Assert.That(JsonSerializer.Serialize(hide.Source.Parameters), Is.EqualTo(beforeStale)); fieldFlyout.Hide();
                page.SelectedEntry = timeline; timelineWindow.Show(); TimelinePump(timelineWindow); history.Clear();
                var session = scope.GetRequiredService<TimelineEditSession>(); Assert.That(session.VisibleNodes, Is.Empty, "The default tree contains authored tracks only.");
                var add = timelineView.FindControl<Button>("AddTimelineObject")!; TimelineClick(timelineWindow, add);
                var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(add)!;
                var objectPicker = (SceneTargetPicker)flyout.Content!; var draft = session.ObjectEdit!;
                var target = draft.Snapshot.Choices.Single(choice => choice.Target.Locator.EntryId == layers[0].StableId);
                objectPicker.ResourcePanel.Search.Text = target.Item.Label; TimelinePump(timelineWindow); PressGroupKey(objectPicker.ResourcePanel.Search, Key.Down);
                Assert.That(objectPicker.ResourcePanel.GetType(), Is.EqualTo(new AssetPicker().ResourcePanel.GetType()));
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                { using var frame = timelineWindow.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "scene-target-select.png")); }
                PressGroupKey(objectPicker.ResourcePanel.Search, Key.Enter); TimelinePump(timelineWindow);
                Assert.That(history.CanUndo, Is.False, "Selecting an object is a local panel draft until a property is authored.");
                Assert.That(timeline.Source.Parameters["timeline"].GetProperty("tracks").GetArrayLength(), Is.EqualTo(0));
                Assert.That(session.VisibleNodes.Count(node => node.IsObject), Is.EqualTo(1));
                Button PropertyButton() => timelineView.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "AddTimelineProperty" && control.IsVisible && control.DataContext is TimelineTreeNode { ObjectHandle: var handle } && handle == target.Target.Handle);
                void AddFromMenu(string property)
                {
                    var plus = PropertyButton(); TimelineClick(timelineWindow, plus);
                    var menu = (MenuFlyout)FlyoutBase.GetAttachedFlyout(plus)!;
                    Assert.That(menu.IsOpen, Is.True);
                    if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } output && property == "transform.x")
                    { using var frame = timelineWindow.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, "scene-property-menu.png")); }
                    menu.Items.OfType<MenuItem>().Single(item => item.DataContext is TimelineValueRow row && row.Property == property).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    menu.Hide(); TimelinePump(timelineWindow);
                }
                AddFromMenu("transform.x");
                Assert.That(timeline.Source.Parameters["timeline"].GetProperty("tracks").GetArrayLength(), Is.EqualTo(1));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(timeline.Source.Parameters["timeline"].GetProperty("tracks").GetArrayLength(), Is.EqualTo(0)); await history.RedoAsync(); TimelinePump(timelineWindow); history.Clear();
                void EditValue(string property, string value)
                {
                    var row = session.Rows.Single(item => item.HasTrack && item.Property == property);
                    var input = timelineView.GetVisualDescendants().OfType<NumericUpDown>().Single(control => ReferenceEquals(control.DataContext, row));
                    var box = input.GetVisualDescendants().OfType<TextBox>().Single(); box.Focus(); box.Text = value; TimelinePump(timelineWindow); PressGroupKey(box, Key.Enter); TimelinePump(timelineWindow);
                }
                EditValue("transform.x", "42"); history.Clear();
                AddFromMenu("opacity"); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); TimelinePump(timelineWindow); history.Clear();
                var opacity = session.Rows.Single(row => row.HasTrack && row.Property == "opacity"); opacity.DraftValue = .5m;
                var text = scope.GetRequiredService<IEditorLocalizationService>();
                foreach (var locale in new[] { "ja-JP", "ko-KR", "en-US", "zh-CN" }) { text.ApplyLocale(locale); TimelinePump(timelineWindow); Assert.That(opacity.DraftValue, Is.EqualTo(.5m)); }
                Assert.That(opacity.Commit(), Is.True); TimelinePump(timelineWindow);
                var root = timeline.Source.Parameters["timeline"]; Assert.That(root.GetProperty("tracks").GetArrayLength(), Is.EqualTo(2));
                Assert.That(root.GetProperty("tracks").EnumerateArray().Single(track => track.GetProperty("property").GetString() == "transform.x").GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(42));
                Assert.That(root.GetProperty("tracks").EnumerateArray().Single(track => track.GetProperty("property").GetString() == "opacity").GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(.5f));
                history.Clear(); TimelineClick(timelineWindow, add); flyout = (Flyout)FlyoutBase.GetAttachedFlyout(add)!;
                objectPicker = (SceneTargetPicker)flyout.Content!; draft = session.ObjectEdit!;
                var other = draft.Snapshot.Choices.Single(choice => choice.Target.Locator.EntryId == layers[1].StableId);
                objectPicker.ResourcePanel.Search.Text = other.Item.Label; TimelinePump(timelineWindow); PressGroupKey(objectPicker.ResourcePanel.Search, Key.Down); PressGroupKey(objectPicker.ResourcePanel.Search, Key.Enter); TimelinePump(timelineWindow);
                Assert.That(history.CanUndo, Is.False);
                Assert.That(timelineView.GetVisualDescendants().OfType<Button>().Any(control => control.Name == "ReplaceTimelineObject"), Is.False);
                Assert.That(timelineView.GetVisualDescendants().OfType<CheckBox>().Any(control => control.Name == "TimelineTrack"), Is.False);
                // A possible object's unknown value stays a visible draft until explicitly entered.
                var otherPlus = timelineView.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "AddTimelineProperty" && control.IsVisible && control.DataContext is TimelineTreeNode { ObjectHandle: var handle } && handle == other.Target.Handle);
                TimelineClick(timelineWindow, otherPlus); var propertyMenu = (MenuFlyout)FlyoutBase.GetAttachedFlyout(otherPlus)!;
                propertyMenu.Items.OfType<MenuItem>().Single(item => item.DataContext is TimelineValueRow row && row.Property == "transform.y").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); propertyMenu.Hide(); TimelinePump(timelineWindow);
                var pending = session.VisibleNodes.Single(node => node.Value?.Property == "transform.y").Value!;
                Assert.That(pending.DraftValue, Is.Null); Assert.That(history.CanUndo, Is.False);
                RemoveTimelineOverride(timelineWindow, timelineView, session.VisibleNodes.Single(node => ReferenceEquals(node.Value, pending)));
                Assert.That(history.CanUndo, Is.False); Assert.That(session.VisibleNodes.Any(node => ReferenceEquals(node.Value, pending)), Is.False);
                Assert.That(session.AddProperty(pending), Is.True); TimelinePump(timelineWindow); pending.DraftValue = 7; Assert.That(pending.Commit(true), Is.True);
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(session.AddProperty(pending), Is.False); history.Clear();
                TimelineClick(timelineWindow, add); flyout = (Flyout)FlyoutBase.GetAttachedFlyout(add)!; objectPicker = (SceneTargetPicker)flyout.Content!;
                PressGroupKey(objectPicker.ResourcePanel.Search, Key.Escape); Assert.That(history.CanUndo, Is.False);
                var stale = session.BeginObjectEdit()!; stale.Select(stale.Choices[0].Id); session.Frame = 5; Assert.That(stale.Commit(), Is.False); Assert.That(history.CanUndo, Is.False);
                var saved = timeline.Source.Parameters["timeline"]; var path = project.RootPath; var groupId = group.Id; var timelineId = timeline.StableId; var hideId = hide.StableId;
                await workspace.SaveAsync(); timelineWindow.Close(); entryWindow.Close(); page.Dispose(); await projects.CloseAsync();
                project = await projects.OpenAsync(path); var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId);
                Assert.That(JsonElement.DeepEquals(saved, reopened.Entries.Single(entry => entry.StableId == timelineId).Source.Parameters["timeline"]), Is.True);
                Assert.That(reopened.Entries.Single(entry => entry.StableId == hideId).Source.Parameters["handleId"].GetString(), Is.EqualTo(first.Target.Handle));
                Assert.That(saved.GetProperty("future")[0].GetRawText(), Is.EqualTo("9007199254740993")); await projects.CloseAsync();
            }
            finally { timelineWindow.Close(); entryWindow.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TimelineOverrideRemovalIsAtomicRejectsStaleRowsAndPreservesOtherRawTracks()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "ReplaceScene"), "Replace", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var layers = workspace.InsertEntriesInto(group, 0, 2, BuiltinEntryIds.ShowLayer);
            var first = layers[0].Source.Parameters["handleId"].GetString()!; var second = layers[1].Source.Parameters["handleId"].GetString()!;
            var entry = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.PlayTimeline).Single();
            var root = JsonSerializer.Deserialize<JsonElement>("""{"durationFrames":10,"future":null,"tracks":[{"handleId":"TARGET","property":"opacity","future":[9007199254740993],"keys":[{"frame":0,"value":1,"interpolationToNext":"CubicHermite","inTangent":2,"outTangent":3,"future":true}]}],"events":[]}""".Replace("TARGET", first, StringComparison.Ordinal));
            entry.Source.Parameters["timeline"] = root;
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var node = System.Text.Json.Nodes.JsonNode.Parse(root.GetRawText())!;
            var tracks = (System.Text.Json.Nodes.JsonArray)node["tracks"]!;
            var extra = tracks[0]!.DeepClone(); extra["property"] = "transform.x"; tracks.Add(extra);
            var other = tracks[0]!.DeepClone(); other["handleId"] = second; tracks.Add(other);
            scope.GetRequiredService<EntryFieldAuthoringService>().SetParameters(group.Id, entry.Source, entry.Type, new() { ["timeline"] = JsonSerializer.SerializeToElement(node) }); history.Clear();
            root = entry.Source.Parameters["timeline"];
            var view = scope.GetRequiredService<TimelinePanelView>(); view.DataContext = scope.GetRequiredService<TimelinePanelViewModel>();
            var window = new Window { Content = view, Width = 1350, Height = 950 }; window.Show(); TimelinePump(window);
            try
            {
                var stale = session.Tree.Single(item => item.ObjectHandle == first);
                RemoveTimelineOverride(window, view, stale);
                var changed = entry.Source.Parameters["timeline"];
                Assert.That(changed.GetProperty("tracks").GetArrayLength(), Is.EqualTo(1));
                Assert.That(JsonElement.DeepEquals(changed.GetProperty("tracks")[0], root.GetProperty("tracks")[2]), Is.True);
                Assert.That(changed.GetProperty("future").ValueKind, Is.EqualTo(JsonValueKind.Null));
                Assert.That(session.VisibleNodes.Any(item => item.ObjectHandle == first), Is.False);
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(JsonElement.DeepEquals(root, entry.Source.Parameters["timeline"]), Is.True);
                Assert.That(session.RemoveOverride(stale), Is.False, "An old row cannot remove tracks after Undo.");
                await history.RedoAsync(); Assert.That(entry.Source.Parameters["timeline"].GetProperty("tracks").GetArrayLength(), Is.EqualTo(1));
                await history.UndoAsync(); history.Clear(); TimelinePump(window);
                var leaf = session.Tree.Single(item => item.ObjectHandle == first).Children.Single(item => item.Value?.Property == "opacity");
                RemoveTimelineOverride(window, view, leaf);
                Assert.That(entry.Source.Parameters["timeline"].GetProperty("tracks").GetArrayLength(), Is.EqualTo(2));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False);
                Assert.That(JsonElement.DeepEquals(root, entry.Source.Parameters["timeline"]), Is.True);
                TimelinePump(window);
                var menu = TimelineOverrideMenu(window, view, session.Tree.Single(item => item.ObjectHandle == first));
                session.Frame = 5;
                menu.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Hide();
                Assert.That(history.CanUndo, Is.False, "An open stale menu cannot address rebuilt track indices.");
                Assert.That(JsonElement.DeepEquals(root, entry.Source.Parameters["timeline"]), Is.True);
                // Removing an empty object or unknown initial-value draft never creates author history.
                var draft = session.BeginObjectEdit()!; draft.Select(draft.Snapshot.Choices.Single(choice => choice.Target.Handle == first).Item.Id); Assert.That(draft.Commit(), Is.True);
                Assert.That(session.RemoveOverride(session.Tree.Single(item => item.ObjectHandle == first)), Is.True);
                history.Clear();
                draft = session.BeginObjectEdit()!; draft.Select(draft.Snapshot.Choices.Single(choice => choice.Target.Handle == first).Item.Id); Assert.That(draft.Commit(), Is.True);
                Assert.That(session.RemoveOverride(session.Tree.Single(item => item.ObjectHandle == first)), Is.True); Assert.That(history.CanUndo, Is.False);
            }
            finally { window.Close(); }
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }
}
