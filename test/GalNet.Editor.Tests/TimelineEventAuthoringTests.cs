using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using GalNet.Game.Controls.Timeline;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private static JsonElement EventAuthorFixture() => JsonSerializer.Deserialize<JsonElement>("""
      {"DurationFrames":20,"FrameRate":60,"future":[9007199254740993,null],"Tracks":[],"Events":[
        {"Frame":3,"Type":"custom.future","future":{"preserve":true}},
        {"Frame":5,"Type":"layer.show","editorId":"creator","tag":"keep","Parameters":{"handleId":"event-object","assetId":"image.png","transform":{"x":7,"opaque":9007199254740993}}},
        {"Frame":5,"Type":"layer.hide","editorId":"consumer","Parameters":{"handleId":"event-object"}},
        {"Frame":15,"Type":"layer.show","editorId":"later","Parameters":{"handleId":"later-object","assetId":"later.png"}}]}
      """);

    [Test]
    public async Task TimelineEventLaneUsesRealPickerAndDragWithAtomicHistoryOrderingUnknownDataAndStaleProtection()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "EventLane"), "Events", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); entry.Source.Parameters["timeline"] = EventAuthorFixture();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var view = new TimelinePanelView { DataContext = scope.GetRequiredService<TimelinePanelViewModel>() }; var window = new Window { Content = view, Width = 1400, Height = 950 }; window.Show(); TimelinePump(window);
            var original = entry.Source.Parameters["timeline"].GetRawText();
            try
            {
                Assert.That(session.Events, Has.Count.EqualTo(4)); Assert.That(history.CanUndo, Is.False);
                Assert.That(session.Events.Single(item => item.Index == 0).CanEdit, Is.False);
                session.Frame = 0; session.NavigateKey(1); Assert.That(session.Frame, Is.EqualTo(3));
                session.NavigateKey(1); Assert.That(session.Frame, Is.EqualTo(5)); session.NavigateKey(-1); Assert.That(session.Frame, Is.EqualTo(3));
                session.Frame = 5; var picker = view.FindControl<EntryTypePicker>("TimelineEventType")!; picker.OpenAt(); TimelinePump(window);
                Assert.That(picker.Results.Select(item => item.Value), Is.EquivalentTo(TimelineEventCapabilities.Types));
                var search = picker.FindControl<TextBox>("SearchInput")!; search.Text = BuiltinEntryIds.ShowLayer; TimelinePump(window); PressGroupKey(search, Key.Down); PressGroupKey(search, Key.Enter); TimelinePump(window);
                Assert.That(session.Events, Has.Count.EqualTo(5)); var added = entry.Source.Parameters["timeline"].GetProperty("Events")[4];
                var eventId = added.GetProperty("editorId").GetString(); var output = added.GetProperty("parameters").GetProperty("handleId").GetString();
                Assert.That(Guid.TryParse(eventId, out _), Is.True); Assert.That(Guid.TryParse(output, out _), Is.True);
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original));
                await history.RedoAsync(); Assert.That(entry.Source.Parameters["timeline"].GetProperty("Events")[4].GetProperty("editorId").GetString(), Is.EqualTo(eventId)); await history.UndoAsync(); TimelinePump(window);
                var canvas = view.FindControl<TimelineCanvas>("TimelineEventKeys")!;
                session.EventCanvas.Select(session.Events.Where(item => item.Index is 1 or 2).Select(item => item.Id));
                var point = CanvasPoint(window, canvas, 1, 5); window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(160, 0));
                Assert.That(history.CanUndo, Is.False); Assert.That(session.EventCanvas.HasPendingMove, Is.True);
                window.MouseUp(point + new Vector(160, 0), MouseButton.Left); TimelinePump(window);
                Assert.That(session.Duration, Is.EqualTo(25)); var events = entry.Source.Parameters["timeline"].GetProperty("Events");
                Assert.That(events[1].GetProperty("Frame").GetInt32(), Is.EqualTo(25)); Assert.That(events[2].GetProperty("Frame").GetInt32(), Is.EqualTo(25));
                Assert.That(events[0].GetProperty("future").GetProperty("preserve").GetBoolean(), Is.True); Assert.That(events[1].GetProperty("tag").GetString(), Is.EqualTo("keep"));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original)); TimelinePump(window);
                point = CanvasPoint(window, canvas, 1, 5); window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(32, 0)); PressGroupKey(canvas, Key.Escape); window.MouseUp(point + new Vector(32, 0), MouseButton.Left);
                Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original));
                session.EventCanvas.Select([session.Events.Single(item => item.Index == 1).Id]); Assert.That(session.EventCanvas.StartMove(), Is.True); session.EventCanvas.UpdateMove(-6);
                Assert.That(session.EventCanvas.IsMoveValid, Is.False); Assert.That(session.EventCanvas.FinishMove(), Is.False); PressGroupKey(canvas, Key.Escape); Assert.That(history.CanUndo, Is.False);
                var consumer = session.Events.Single(item => item.Index == 2); session.OrderEvent(consumer, -1);
                Assert.That(entry.Source.Parameters["timeline"].GetProperty("Events")[1].GetProperty("editorId").GetString(), Is.EqualTo("consumer"));
                Assert.That(session.Events.Single(item => item.Index == 1).IsSelected, Is.True);
                using (var before = session.BeginEventEdit(session.Events.Single(item => item.Index == 1))!)
                    Assert.That(before.Field("handleId").Options!.Select(option => option.Label).OfType<SceneFieldChoice>().Any(choice => choice.Choice?.Target.Handle == "event-object"), Is.False);
                session.EndEventEdit(); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False);
                var insertion = session.CaptureEventInsertion(); var draft = session.BeginEventEdit(session.Events.Single(item => item.Index == 1))!; session.Frame = 6;
                Assert.That(insertion(BuiltinEntryIds.ShowLayer), Is.False); Assert.That(draft.Apply(), Is.False); Assert.That(history.CanUndo, Is.False);
                session.EventCanvas.Select(session.Events.Where(item => item.Index is 0 or 2).Select(item => item.Id)); session.DeleteSelectedEvents();
                Assert.That(session.Events, Has.Count.EqualTo(2)); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original));
                session.Canvas.PixelsPerFrame = 12; session.Canvas.StartFrame = 2; Assert.That(session.EventCanvas.PixelsPerFrame, Is.EqualTo(12)); Assert.That(session.EventCanvas.StartFrame, Is.EqualTo(2));
                session.EventCanvas.PixelsPerFrame = 10; session.EventCanvas.StartFrame = 1; Assert.That(session.Canvas.PixelsPerFrame, Is.EqualTo(10)); Assert.That(session.Canvas.StartFrame, Is.EqualTo(1));
                var localization = scope.GetRequiredService<IEditorLocalizationService>(); foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" }) { localization.ApplyLocale(locale); Assert.That(session.Events[0].Label, Does.Contain(localization.Format("Timeline.Event.Label", 3, "custom.future"))); }
                var root = project.RootPath; var groupId = group.Id; var entryId = entry.StableId; await workspace.SaveAsync(); window.Close(); page.Dispose(); await projects.CloseAsync();
                project = await projects.OpenAsync(root); var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == entryId).Source.Parameters["timeline"];
                Assert.That(JsonElement.DeepEquals(reopened, EventAuthorFixture()), Is.True); await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TimelineEventInspectorHidesOutputsUsesEventBeforePickerAndAppliesOneCancelableDraft()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "EventForm"), "Event form", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); entry.Source.Parameters["timeline"] = EventAuthorFixture();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var panel = scope.GetRequiredService<TimelinePanelViewModel>(); var view = new TimelinePanelView { DataContext = panel };
            using var inspector = new TimelineEventInspectorViewModel(panel); var body = new TimelineEventInspectorView { DataContext = inspector, Width = 520 };
            var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,520") }; layout.Children.Add(view); Grid.SetColumn(body, 1); layout.Children.Add(body);
            var window = new Window { Content = layout, Width = 1400, Height = 950 }; window.Show(); TimelinePump(window);
            try
            {
                var row = session.Events.Single(item => item.Index == 1); session.SelectEvent(row); TimelinePump(window);
                var host = body.GetVisualDescendants().OfType<DtoEditorHost>().Single();
                using (var lockedInspector = new TimelineEventInspectorViewModel(panel))
                {
                    lockedInspector.SetLocked(true); var lockedDraft = lockedInspector.Draft;
                    session.SelectEvent(session.Events.Single(item => item.Index == 2));
                    Assert.That(lockedInspector.Draft, Is.SameAs(lockedDraft)); Assert.That(lockedDraft!.IsCurrent, Is.True);
                    session.SelectEvent(row); TimelinePump(window);
                }
                Assert.That(inspector.Draft!.IsCurrent, Is.True);
                Assert.That(host.Diagnostics, Is.Empty); Assert.That(inspector.Draft!.Field("handleId").IsVisible, Is.False);
                Assert.That(host.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Diagnostics), Is.Empty);
                Assert.That(host.GetVisualDescendants().OfType<AssetPicker>().Single().Filter, Is.EqualTo(GalNet.Editor.Models.AssetPickerFilter.Image));
                var x = NumericControlTestSupport.NamedInputs(host, "TransformX").Single(); x.Focus(); x.Text = "13"; PressGroupKey(x, Key.Enter); TimelinePump(window);
                Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetProperty("Events")[1].GetProperty("Parameters").GetProperty("transform").GetProperty("x").GetSingle(), Is.EqualTo(7));
                TimelineClick(window, body.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ApplyTimelineEvent")); TimelinePump(window);
                var parameters = entry.Source.Parameters["timeline"].GetProperty("Events")[1].GetProperty("Parameters");
                Assert.That(parameters.GetProperty("transform").GetProperty("x").GetSingle(), Is.EqualTo(13)); Assert.That(parameters.GetProperty("transform").GetProperty("opaque").GetRawText(), Is.EqualTo("9007199254740993"));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); history.Clear(); TimelinePump(window);
                row = session.Events.Single(item => item.Index == 2); session.SelectEvent(row); TimelinePump(window);
                host = body.GetVisualDescendants().OfType<DtoEditorHost>().Single();
                var targetButton = host.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "SceneTargetField"); Assert.That(targetButton.Content?.ToString(), Does.Contain("image.png")); TimelineClick(window, targetButton);
                var picker = (SceneTargetPicker)((Flyout)targetButton.Flyout!).Content!;
                Assert.That(picker.ResourcePanel.Results.Any(item => item.Label.Contains("image.png", StringComparison.Ordinal)), Is.True);
                Assert.That(picker.ResourcePanel.Results.Any(item => item.Label.Contains("later.png", StringComparison.Ordinal)), Is.False);
                PressGroupKey(picker.ResourcePanel.Search, Key.Escape); TimelinePump(window);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "events-b-form.png")); }
                var stale = inspector.Draft!; session.Frame = 2; TimelinePump(window); Assert.That(stale.Apply(), Is.False); Assert.That(history.CanUndo, Is.False);
                var pending = session.BeginEventEdit(session.Events.Single(item => item.Index == 1))!;
                await pending.Field("transform").CommitAsync(new(JsonSerializer.SerializeToElement(new { x = 99 })), CancellationToken.None); session.EndEventEdit(); Assert.That(pending.Apply(), Is.False); Assert.That(history.CanUndo, Is.False);
                foreach (var type in TimelineEventCapabilities.Types)
                {
                    Assert.That(session.AddEvent(type), Is.True); TimelinePump(window);
                    row = session.Events.Single(item => item.Index == 4); session.SelectEvent(row); TimelinePump(window);
                    Assert.That(body.GetVisualDescendants().OfType<DtoEditorHost>().Single().Diagnostics, Is.Empty, type);
                    Assert.That(body.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Diagnostics), Is.Empty, type);
                    TimelineClick(window, body.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "CancelTimelineEvent"));
                    await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                }
                Assert.That(session.AddEvent(BuiltinEntryIds.ShowLayer), Is.True);
                var undone = session.BeginEventEdit(session.Events.Single(item => item.Index == 4))!;
                await history.UndoAsync(); Assert.That(undone.Apply(), Is.False); Assert.That(history.CanUndo, Is.False);
                var other = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.Wait).Single(); page.SelectedEntry = entry; history.Clear();
                var switched = session.BeginEventEdit(session.Events.Single(item => item.Index == 1))!;
                page.SelectedEntry = other; Assert.That(switched.Apply(), Is.False); Assert.That(session.EventEdit, Is.Null); Assert.That(session.Events, Is.Empty);
                page.SelectedEntry = entry; session.EventCanvas.Select([session.Events.Single(item => item.Index == 1).Id]);
                Assert.That(session.EventCanvas.StartMove(), Is.True); session.EventCanvas.UpdateMove(2); page.SelectedEntry = other;
                Assert.That(session.EventCanvas.HasPendingMove, Is.False); Assert.That(session.EventCanvas.Rows, Is.Empty); Assert.That(history.CanUndo, Is.False); page.SelectedEntry = entry;
                var rootPath = project.RootPath; var groupId = group.Id; var entryId = entry.StableId;
                await workspace.SaveAsync();
                var onClose = session.BeginEventEdit(session.Events.Single(item => item.Index == 1))!;
                window.Close(); page.Dispose(); await projects.CloseAsync(); Assert.That(onClose.Apply(), Is.False);
                project = await projects.OpenAsync(rootPath);
                var saved = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == entryId).Source.Parameters["timeline"];
                Assert.That(saved.GetProperty("Events")[1].GetProperty("Parameters").GetProperty("transform").GetProperty("x").GetSingle(), Is.EqualTo(13));
                Assert.That(saved.GetProperty("Events")[1].GetProperty("Parameters").GetProperty("transform").GetProperty("opaque").GetRawText(), Is.EqualTo("9007199254740993"));
                await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
