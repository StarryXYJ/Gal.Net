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
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Timeline;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private static MenuFlyout TimelineKeyMenu(Window window, TimelinePanelView view, TimelineEditSession session)
    {
        var canvas = view.FindControl<TimelineCanvas>("TimelineKeys")!;
        if (FlyoutBase.GetAttachedFlyout(canvas) is PopupFlyoutBase previous)
        {
            var editor = (previous as Flyout)?.Content as TimelineKeyEditorView;
            previous.Hide();
            if (editor is not null) { Assert.That(editor.DataContext, Is.Null); Assert.That(((Flyout)previous).Content, Is.Null); }
        }
        TimelinePump(window);
        var row = session.Canvas.Rows.ToList().FindIndex(row => !row.IsSummary && row.Keys.Any(key => session.Canvas.Selection.Contains(key.Id)));
        var key = session.Canvas.Rows[row].Keys.First(key => session.Canvas.Selection.Contains(key.Id));
        var point = canvas.TranslatePoint(new Point((key.Frame - session.Canvas.StartFrame) * session.Canvas.PixelsPerFrame, session.Canvas.RowTop(row) + 19), window)!.Value;
        window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); TimelinePump(window);
        var menu = (MenuFlyout)FlyoutBase.GetAttachedFlyout(canvas)!; Assert.That(menu.IsOpen, Is.True); return menu;
    }
    private static TimelineKeyEditorView OpenTimelineKeyEditor(Window window, TimelinePanelView view, TimelineEditSession session)
    {
        var menu = TimelineKeyMenu(window, view, session);
        menu.Items.OfType<MenuItem>().Single(item => item.Name == "TimelineEditKeyValues").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); TimelinePump(window);
        var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(view.FindControl<TimelineCanvas>("TimelineKeys")!)!;
        Assert.That(flyout.IsOpen, Is.True); return (TimelineKeyEditorView)flyout.Content!;
    }
    [Test]
    public async Task TimelineKeyDetailsBatchEditInterpolationTangentsUnitsAndPresetsThroughMountedControls()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "KeyDetails"), "Details", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); entry.Source.Parameters["timeline"] = KeyCanvasFixture();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            var session = scope.GetRequiredService<TimelineEditSession>(); var view = new TimelinePanelView { DataContext = scope.GetRequiredService<TimelinePanelViewModel>() };
            var window = new Window { Content = view, Width = 1500, Height = 1000 }; window.Show(); TimelinePump(window);
            try
            {
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear(); var canvas = view.FindControl<TimelineCanvas>("TimelineKeys")!;
                int Row() => session.VisibleNodes.IndexOf(session.VisibleNodes.Single(node => node.Value is { HasTrack: true, Property: "transform.x" }));
                void Select(params int[] frames) { session.Canvas.Select(session.Canvas.Rows[Row()].Keys.Where(key => frames.Contains(key.Frame)).Select(key => key.Id)); TimelinePump(window); }
                var point = CanvasPoint(window, canvas, Row(), 10); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); TimelinePump(window);
                Assert.That(CanvasPoint(window, canvas, Row(), 10).Y, Is.EqualTo(point.Y), "Showing key details must not shift a captured pointer's canvas.");
                point = CanvasPoint(window, canvas, Row(), 20); window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control); window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control); TimelinePump(window);
                Assert.That(session.KeyEdit!.Count, Is.EqualTo(2)); Assert.That(session.KeyEdit.Value, Is.Null); Assert.That(session.KeyEdit.Incoming, Is.Null); Assert.That(session.KeyEdit.InterpolationIndex, Is.EqualTo(-1));
                Assert.That(history.CanUndo, Is.False, "Opening mixed/default controls must not write author data.");
                var original = entry.Source.Parameters["timeline"];
                void Input(string name, string text)
                {
                    var numeric = OpenTimelineKeyEditor(window, view, session).FindControl<NumericUpDown>(name)!; var box = numeric.GetVisualDescendants().OfType<TextBox>().Single();
                    box.Focus(); box.Text = text; TimelinePump(window); PressGroupKey(box, Key.Enter); TimelinePump(window);
                }
                Input("TimelineKeyIncoming", "6");
                JsonElement XKeys() => entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys");
                Assert.That(XKeys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(6)); Assert.That(XKeys()[2].GetProperty("InTangent").GetSingle(), Is.EqualTo(6));
                Assert.That(XKeys()[1].GetProperty("futureKey")[0].GetInt32(), Is.EqualTo(1)); Assert.That(session.KeyEdit!.Count, Is.EqualTo(2));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(JsonElement.DeepEquals(original, entry.Source.Parameters["timeline"]), Is.True); await history.RedoAsync(); Select(10, 20); history.Clear();
                var old = session.KeyEdit!; old.Value = 99;
                OpenTimelineKeyEditor(window, view, session).FindControl<ComboBox>("TimelineKeyInterpolation")!.SelectedIndex = 0; TimelinePump(window);
                Assert.That(XKeys()[1].GetProperty("InterpolationToNext").GetString(), Is.EqualTo("Step")); Assert.That(XKeys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(6)); Assert.That(XKeys()[1].GetProperty("OutTangent").GetSingle(), Is.EqualTo(2)); Assert.That(old.CommitValue(), Is.False);
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Select(10, 20);
                OpenTimelineKeyEditor(window, view, session).FindControl<ComboBox>("TimelineKeyInterpolation")!.SelectedIndex = 1; TimelinePump(window);
                Assert.That(XKeys()[1].GetProperty("InterpolationToNext").GetString(), Is.EqualTo("Linear")); Assert.That(XKeys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(6)); history.Clear();
                session.TangentsPerSecond = true; Assert.That(session.KeyEdit!.Incoming, Is.EqualTo(360)); Input("TimelineKeyIncoming", "120");
                Assert.That(XKeys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(2)); Assert.That(XKeys()[2].GetProperty("InTangent").GetSingle(), Is.EqualTo(2));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); Select(10, 20);
                session.TimingFrameRateDraft = 30; session.CommitTimingInput(true); Select(10, 20); Assert.That(session.KeyEdit!.Incoming, Is.EqualTo(60)); history.Clear();
                Assert.That(session.KeyEdit!.ApplyPreset(TimelineTangentPreset.Flat), Is.True);
                Assert.That(XKeys()[1].GetProperty("InterpolationToNext").GetString(), Is.EqualTo("CubicHermite")); Assert.That(XKeys()[1].GetProperty("OutTangent").GetSingle(), Is.EqualTo(0)); Assert.That(XKeys()[2].GetProperty("InTangent").GetSingle(), Is.EqualTo(0));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Select(10, 20);
                Assert.That(session.KeyEdit!.ApplyPreset(TimelineTangentPreset.Linear), Is.True); Assert.That(XKeys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(1)); Assert.That(XKeys()[2].GetProperty("outTangent").GetSingle(), Is.EqualTo(1));
                await history.UndoAsync(); Select(10, 20); history.Clear();
                Assert.That(session.KeyEdit!.ApplyPreset(TimelineTangentPreset.Smooth), Is.True); Assert.That(XKeys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(1)); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Select(10, 20);
                Assert.That(session.KeyEdit!.ApplyPreset(TimelineTangentPreset.Aligned), Is.True); Assert.That(XKeys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(2)); Assert.That(XKeys()[2].GetProperty("outTangent").GetSingle(), Is.EqualTo(1)); history.Clear();
                // Value editing includes the protected first key but never changes its frame.
                Select(0, 10); Input("TimelineKeyValue", "8"); Assert.That(XKeys()[0].GetProperty("Value").GetSingle(), Is.EqualTo(8)); Assert.That(XKeys()[1].GetProperty("Value").GetSingle(), Is.EqualTo(8)); Assert.That(XKeys()[0].GetProperty("Frame").GetInt32(), Is.EqualTo(0));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Select(10, 20);
                var stale = session.KeyEdit!; stale.Outgoing = 55; session.Frame = 5; Assert.That(stale.CommitOutgoing(), Is.False); Assert.That(history.CanUndo, Is.False);
                var draft = session.KeyEdit!; var beforeDraft = draft.Incoming; draft.Incoming = 13; var locale = scope.GetRequiredService<IEditorLocalizationService>();
                foreach (var culture in new[] { "ja-JP", "ko-KR", "en-US", "zh-CN" }) { locale.ApplyLocale(culture); TimelinePump(window); Assert.That(draft.Incoming, Is.EqualTo(13)); Assert.That(session.KeyEdit, Is.SameAs(draft)); }
                draft.Cancel(); Assert.That(draft.Incoming, Is.EqualTo(beforeDraft));
                if (FlyoutBase.GetAttachedFlyout(canvas) is PopupFlyoutBase opened) opened.Hide();
                var saved = entry.Source.Parameters["timeline"]; var path = project.RootPath; var groupId = group.Id; var entryId = entry.StableId;
                await workspace.SaveAsync(); Assert.That(session.KeyEdit, Is.SameAs(draft)); Assert.That(history.CanUndo, Is.False);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } output) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, "timeline-key-details.png")); }
                window.Close(); page.Dispose(); await projects.CloseAsync(); Assert.That(draft.CommitIncoming(), Is.False);
                project = await projects.OpenAsync(path); var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == entryId).Source.Parameters["timeline"];
                Assert.That(JsonElement.DeepEquals(saved, reopened), Is.True); Assert.That(reopened.GetProperty("opaque").GetProperty("big").GetRawText(), Is.EqualTo("9007199254740993")); await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TimelineMixedTrackValueEditRejectsOneInvalidEndpointAtomically()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "MixedRange"), "Range", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var creator = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single(); var handle = creator.Source.Parameters["handleId"].GetString();
        var entry = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.PlayTimeline).Single();
        entry.Source.Parameters["timeline"] = JsonSerializer.SerializeToElement(new
        {
            durationFrames = 10,
            tracks = new[]
        {
            new { handleId = handle, property = "opacity", keys = new[] { new { frame = 0, value = 1f }, new { frame = 10, value = .5f } } },
            new { handleId = handle, property = "transform.x", keys = new[] { new { frame = 0, value = 0f }, new { frame = 10, value = 5f } } }
        },
            events = Array.Empty<object>()
        });
        using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
        var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        session.Canvas.Select(session.Canvas.Rows.SelectMany(row => row.Keys.Select(key => key.Id)));
        var original = entry.Source.Parameters["timeline"]; session.KeyEdit!.Value = 2; Assert.That(session.KeyEdit.CommitValue(), Is.False);
        Assert.That(history.CanUndo, Is.False); Assert.That(JsonElement.DeepEquals(original, entry.Source.Parameters["timeline"]), Is.True);
        Assert.That(session.Canvas.StartCurve(), Is.True); session.Canvas.UpdateCurve(new(ValueDelta: 1));
        Assert.That(session.Canvas.IsMoveValid, Is.False); session.Canvas.FinishCurve();
        Assert.That(session.Canvas.HasPendingCurve, Is.False); Assert.That(history.CanUndo, Is.False); Assert.That(JsonElement.DeepEquals(original, entry.Source.Parameters["timeline"]), Is.True);
        Assert.That(session.Canvas.StartCurve(), Is.True); session.Canvas.UpdateCurve(new(Tangent: float.NaN));
        Assert.That(session.Canvas.IsMoveValid, Is.False); session.Canvas.Cancel(); Assert.That(history.CanUndo, Is.False);
        Assert.That(session.Canvas.StartCurve(), Is.True); session.Canvas.UpdateCurve(new(ValueDelta: -.1f));
        session.Canvas.Select(session.Canvas.Selection.Take(1).ToArray()); Assert.That(session.Canvas.HasPendingCurve, Is.False);
        session.Canvas.Select(session.Canvas.Rows.SelectMany(row => row.Keys.Select(key => key.Id)));
        session.KeyEdit.Value = .25m; Assert.That(session.KeyEdit.CommitValue(), Is.True);
        Assert.That(entry.Source.Parameters["timeline"].GetProperty("tracks").EnumerateArray().SelectMany(track => track.GetProperty("keys").EnumerateArray()).All(key => key.GetProperty("value").GetSingle() == .25f), Is.True);
        await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(JsonElement.DeepEquals(original, entry.Source.Parameters["timeline"]), Is.True);
        await projects.CloseAsync();
    }

    [Test]
    public async Task TimelineTangentPresetRejectsNonFiniteDerivedSlopeWithoutPartialHistory()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "SlopeOverflow"), "Overflow", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single();
        entry.Source.Parameters["timeline"] = JsonSerializer.Deserialize<JsonElement>("""{"durationFrames":1,"tracks":[{"handleId":"target","property":"transform.x","keys":[{"frame":0,"value":-3e38},{"frame":1,"value":3e38}]}],"events":[]}""");
        using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
        var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        session.Canvas.Select(session.Canvas.Rows.SelectMany(row => row.Keys.Select(key => key.Id)));
        var original = entry.Source.Parameters["timeline"]; Assert.That(session.KeyEdit!.ApplyPreset(TimelineTangentPreset.Linear), Is.False);
        Assert.That(history.CanUndo, Is.False); Assert.That(JsonElement.DeepEquals(original, entry.Source.Parameters["timeline"]), Is.True); Assert.That(session.Message, Is.Not.Empty);
        await projects.CloseAsync();
    }
}
