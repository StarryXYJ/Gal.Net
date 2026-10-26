using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Timeline;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private static MenuItem CurveMenuItem(MenuFlyout menu, string name)
    {
        IEnumerable<MenuItem> Flatten(IEnumerable<object?> items) => items.OfType<MenuItem>().SelectMany(item => new[] { item }.Concat(Flatten(item.Items)));
        return Flatten(menu.Items).Single(item => item.Name == name);
    }
    [Test]
    public async Task InlineTimelineCurvesUseRealMenusKeysAndHandlesWithAtomicHistoryAndCancellation()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "InlineCurve"), "Curves", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); entry.Source.Parameters["timeline"] = KeyCanvasFixture();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            var session = scope.GetRequiredService<TimelineEditSession>(); var view = new TimelinePanelView { DataContext = scope.GetRequiredService<TimelinePanelViewModel>() };
            var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var window = new Window { Content = view, Width = 1500, Height = 1000 }; window.Show(); TimelinePump(window);
            try
            {
                var canvas = view.FindControl<TimelineCanvas>("TimelineKeys")!;
                TimelineTreeNode Leaf() => session.VisibleNodes.Single(node => node.Value?.Property == "transform.x");
                int Row() => session.VisibleNodes.IndexOf(Leaf());
                void Select(params int[] frames) { session.Canvas.Select(session.Canvas.Rows[Row()].Keys.Where(key => frames.Contains(key.Frame)).Select(key => key.Id)); TimelinePump(window); }
                JsonElement Keys() => entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys");
                void ClickMenu(string name)
                { var menu = TimelineKeyMenu(window, view, session); CurveMenuItem(menu, name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); TimelinePump(window); }
                Select(0, 10, 20); ClickMenu("TimelineModeBothAuto");
                Assert.That(Keys().EnumerateArray().All(key => key.GetProperty("editorTangents").GetProperty("incoming").GetString() == "Auto"), Is.True);
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); history.Clear(); Select(10);
                var oldHeight = session.Canvas.ContentHeight;
                var expander = view.GetVisualDescendants().OfType<ToggleButton>().Single(control => control.Name == "TimelineExpand" && ReferenceEquals(control.DataContext, Leaf())); TimelineClick(window, expander);
                Assert.That(Leaf().HasCurve, Is.True); Assert.That(session.Canvas.ContentHeight - oldHeight, Is.EqualTo(TimelineCurveViewport.Height));
                Assert.That(view.FindControl<NumericUpDown>("TimelineKeyIncoming"), Is.Null, "No independent key-details region remains.");
                Point Position(int frame)
                {
                    var key = session.Canvas.Rows[Row()].Keys.Single(key => key.Frame == frame); var viewport = session.Canvas.Rows[Row()].Curve!;
                    return canvas.TranslatePoint(new Point((frame - session.Canvas.StartFrame) * session.Canvas.PixelsPerFrame, session.Canvas.RowTop(Row()) + 38 + viewport.Y(key.Value)), window)!.Value;
                }
                var yInput = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.Name == "TimelineValue" && control.DataContext is TimelineValueRow { Property: "transform.y" });
                var yRow = session.VisibleNodes.ToList().FindIndex(node => node.Value?.Property == "transform.y");
                Assert.That(yInput.TranslatePoint(new Point(0, yInput.Bounds.Height / 2), window)!.Value.Y, Is.EqualTo(canvas.TranslatePoint(new Point(0, session.Canvas.RowTop(yRow) + 19), window)!.Value.Y).Within(1));
                var before = entry.Source.Parameters["timeline"].GetRawText(); var point = Position(10);
                window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(8, -10));
                Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(before)); Assert.That(session.Canvas.HasPendingCurve, Is.True);
                window.MouseUp(point + new Vector(8, -10), MouseButton.Left); TimelinePump(window);
                Assert.That(Keys()[1].GetProperty("Frame").GetInt32(), Is.EqualTo(11)); Assert.That(Keys()[1].GetProperty("Value").GetSingle(), Is.GreaterThan(10));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(before)); await history.RedoAsync(); await history.UndoAsync(); TimelinePump(window);
                Select(10); point = Position(10); window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(8, -10)); PressGroupKey(canvas, Key.Escape); window.MouseUp(point, MouseButton.Left);
                Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(before));
                // Tangent handles edit a linked pair and leave a persistent free mode behind.
                Select(10); point = Position(10); var viewport = session.Canvas.Rows[Row()].Curve!;
                var slope = -1d * 144 / viewport.Range / session.Canvas.PixelsPerFrame; var dx = 44 / Math.Sqrt(1 + slope * slope);
                var handle = point + new Vector(dx, dx * slope);
                window.MouseDown(handle, MouseButton.Left); window.MouseMove(handle + new Vector(0, -12)); window.MouseUp(handle + new Vector(0, -12), MouseButton.Left); TimelinePump(window);
                Assert.That(Keys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(Keys()[1].GetProperty("OutTangent").GetSingle()));
                Assert.That(Keys()[1].GetProperty("OutTangent").GetSingle(), Is.GreaterThan(1));
                Assert.That(Keys()[1].GetProperty("editorTangents").GetProperty("outgoing").GetString(), Is.EqualTo("Free"));
                Assert.That(Keys()[1].GetProperty("futureKey")[1].ValueKind, Is.EqualTo(JsonValueKind.Null));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                Select(0); point = Position(0) + new Vector(1, 0); window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(16, 0)); window.MouseUp(point + new Vector(16, 0), MouseButton.Left);
                Assert.That(history.CanUndo, Is.False); Assert.That(session.Canvas.HasPendingCurve, Is.False); Assert.That(Keys()[0].GetProperty("Frame").GetInt32(), Is.Zero);
                point = Position(0) + new Vector(1, 0); window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(0, -6)); window.MouseUp(point + new Vector(0, -6), MouseButton.Left); TimelinePump(window);
                Assert.That(Keys()[0].GetProperty("Value").GetSingle(), Is.GreaterThan(0)); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                // Multiple selected keys move by the same value delta, without flattening their values.
                Select(10); point = Position(20); window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control); window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
                Assert.That(session.Canvas.Selection, Has.Count.EqualTo(2)); point = Position(10);
                window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(0, -8)); window.MouseUp(point + new Vector(0, -8), MouseButton.Left); TimelinePump(window);
                Assert.That(Keys()[2].GetProperty("Value").GetSingle() - Keys()[1].GetProperty("Value").GetSingle(), Is.EqualTo(10).Within(.00001));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                Select(10); point = Position(10); window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(8, -4));
                session.Frame = 5; window.MouseUp(point + new Vector(8, -4), MouseButton.Left);
                Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(before)); Assert.That(history.CanUndo, Is.False);
                Select(10); var text = scope.GetRequiredService<IEditorLocalizationService>();
                foreach (var culture in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    text.ApplyLocale(culture); TimelinePump(window); var menu = TimelineKeyMenu(window, view, session);
                    Assert.That(CurveMenuItem(menu, "TimelineModeBothAuto").Header, Is.EqualTo(text["Timeline.Curve.Auto"])); menu.Hide();
                }
                var cameraBeforePan = session.Canvas.Rows[Row()].Curve!.Minimum;
                var pan = canvas.TranslatePoint(new Point(400, session.Canvas.RowTop(Row()) + 90), window)!.Value;
                window.MouseDown(pan, MouseButton.Left, RawInputModifiers.Alt); window.MouseMove(pan + new Vector(0, 12)); window.MouseUp(pan + new Vector(0, 12), MouseButton.Left, RawInputModifiers.Alt);
                Assert.That(session.Canvas.Rows[Row()].Curve!.Minimum, Is.GreaterThan(cameraBeforePan)); Assert.That(history.CanUndo, Is.False);
                var fit = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "FitTimelineCurve" && ReferenceEquals(control.DataContext, Leaf()));
                viewport = session.Canvas.Rows[Row()].Curve!; viewport.Zoom(2, 10); var previousRange = viewport.Range; TimelineClick(window, fit);
                Assert.That(viewport.Range, Is.LessThan(previousRange)); Assert.That(history.CanUndo, Is.False);
                var open = TimelineKeyMenu(window, view, session);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } output) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, "timeline-inline-curve-menu.png")); }
                open.Hide(); var saved = entry.Source.Parameters["timeline"]; var path = project.RootPath; var groupId = group.Id; var entryId = entry.StableId;
                await workspace.SaveAsync(); window.Close(); page.Dispose(); await projects.CloseAsync(); project = await projects.OpenAsync(path);
                var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == entryId).Source.Parameters["timeline"];
                Assert.That(JsonElement.DeepEquals(saved, reopened), Is.True); await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
    [Test]
    public async Task TimelineAutomaticModesRecalculateNeighborsAndRetainMetadataAcrossValueMoveAddAndDelete()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "AutoTangents"), "Automatic", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single();
        var raw = System.Text.Json.Nodes.JsonNode.Parse(KeyCanvasFixture().GetRawText())!;
        raw["Tracks"]![0]!["Keys"]![1]!["editorTangents"] = System.Text.Json.Nodes.JsonNode.Parse("""{"incoming":"Free","outgoing":"Free","linked":false,"future":[9007199254740993,null]}""");
        entry.Source.Parameters["timeline"] = JsonSerializer.SerializeToElement(raw);
        using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
        var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
        int Row() => session.VisibleNodes.ToList().FindIndex(node => node.Value?.Property == "transform.x");
        void Select(params int[] frames) => session.Canvas.Select(session.Canvas.Rows[Row()].Keys.Where(key => frames.Contains(key.Frame)).Select(key => key.Id));
        JsonElement Keys() => entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys");
        Select(0, 10, 20); Assert.That(session.KeyEdit!.SetMode(TimelineTangentMode.Auto), Is.True); history.Clear();
        session.Frame = 20; var row = session.Rows.Single(row => row.Property == "transform.x"); row.DraftValue = 50; Assert.That(row.Commit(), Is.True);
        Assert.That(Keys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(2.5));
        Assert.That(Keys()[1].GetProperty("editorTangents").GetProperty("future")[0].GetRawText(), Is.EqualTo("9007199254740993"));
        await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); history.Clear();
        Select(20); Assert.That(session.Canvas.StartMove(), Is.True); session.Canvas.UpdateMove(10); Assert.That(session.Canvas.FinishMove(), Is.True);
        Assert.That(Keys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(50f / 30).Within(.00001));
        await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); history.Clear();
        Select(20); session.DeleteSelectedKeys(); Assert.That(Keys()[1].GetProperty("InTangent").GetSingle(), Is.EqualTo(1)); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False);
        session.Frame = 5; session.SetActiveTrack(session.Rows.Single(row => row.Property == "transform.x")); session.AddCurrentKey(); Assert.That(Keys().GetArrayLength(), Is.EqualTo(4));
        await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); history.Clear();
        Select(10); Assert.That(session.KeyEdit!.SetMode(TimelineTangentMode.Flat, true), Is.True);
        Assert.That(Keys()[1].GetProperty("InTangent").GetSingle(), Is.Zero); Assert.That(Keys()[1].GetProperty("OutTangent").GetSingle(), Is.EqualTo(2.5));
        Assert.That(session.KeyEdit!.TangentModes.Single().Linked, Is.False); await history.UndoAsync(); history.Clear();
        Select(10); var stale = session.KeyEdit!; Assert.That(stale.SetLinked(false), Is.True); Assert.That(stale.SetMode(TimelineTangentMode.Free), Is.False);
        var track = TimelineCurveViewport.Track(session.Canvas.Rows[Row()].Keys);
        var originalTrack = JsonSerializer.Deserialize<TimelineTrackDefinition>(entry.Source.Parameters["timeline"].GetProperty("Tracks")[0], new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;
        foreach (var frame in new[] { 0d, 1.25, 5.5, 10, 15.75, 20 }) Assert.That(TimelineSampler.Evaluate(track, frame), Is.EqualTo(TimelineSampler.Evaluate(originalTrack, frame)));
        var camera = new TimelineCurveViewport(); camera.Fit(session.Canvas.Rows[Row()].Keys);
        Assert.That(camera.Maximum, Is.GreaterThan(50)); Assert.That(camera.Minimum, Is.LessThanOrEqualTo(0));
        await projects.CloseAsync();
    }
    [Test]
    public void TimelineCurveViewportIncludesCubicExtremaAndRetainsItsZoomAnchor()
    {
        TimelineCanvasKey[] keys = [new(Guid.NewGuid(), 0, 0, OutTangent: 4, Interpolation: AnimationInterpolation.CubicHermite), new(Guid.NewGuid(), 10, 0, InTangent: -4)];
        var track = TimelineCurveViewport.Track(keys); Assert.That(TimelineSampler.Evaluate(track, 5), Is.EqualTo(10));
        var camera = new TimelineCurveViewport(); camera.Fit(keys);
        Assert.That(camera.Maximum, Is.GreaterThan(10)); Assert.That(camera.Minimum, Is.LessThan(0));
        var y = camera.Y(3); camera.Zoom(.5, 3); Assert.That(camera.Y(3), Is.EqualTo(y).Within(.00001));
        var before = camera.Range; camera.Zoom(double.PositiveInfinity, 3); Assert.That(camera.Range, Is.EqualTo(before));
    }

}
