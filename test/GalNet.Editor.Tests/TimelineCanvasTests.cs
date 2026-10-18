using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
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
    private static JsonElement KeyCanvasFixture() => JsonSerializer.Deserialize<JsonElement>("""
        {"FrameRate":60,"DurationFrames":60,"opaque":{"big":9007199254740993},"Tracks":[
          {"HandleId":"private-target","Property":"transform.x","futureTrack":"keep","Keys":[
            {"Frame":0,"Value":0,"first":"keep"},
            {"Frame":10,"Value":10,"InTangent":1,"OutTangent":2,"InterpolationToNext":"CubicHermite","futureKey":[1,null]},
            {"Frame":20,"Value":20,"InTangent":3}]},
          {"HandleId":"private-target","Property":"transform.y","Keys":[{"Frame":0,"Value":0},{"Frame":10,"Value":5},{"Frame":20,"Value":15}]}],
          "Events":[{"Frame":12,"Type":"future.a","tag":"a"},{"Frame":12,"Type":"future.b","tag":"b"},{"Frame":59,"Type":"future.c","tag":"late"}]}
        """);
    private static Point CanvasPoint(Window window, TimelineCanvas canvas, int row, int frame) => canvas.TranslatePoint(
        new Point((frame - canvas.Model!.StartFrame) * canvas.Model.PixelsPerFrame, (row + .5) * canvas.Model.RowHeight), window)!.Value;

    [Test]
    public async Task TimelineCanvasRealPointerGesturesAlignSelectMoveCancelReplaceProtectAndRejectStaleTargets()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TimelineCanvas"), "Canvas", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); entry.Source.Parameters["timeline"] = KeyCanvasFixture();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            var session = scope.GetRequiredService<TimelineEditSession>(); var vm = scope.GetRequiredService<TimelinePanelViewModel>(); var view = new TimelinePanelView { DataContext = vm };
            var window = new Window { Content = view, Width = 1400, Height = 900 }; window.Show(); TimelinePump(window);
            try
            {
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                var canvas = view.FindControl<TimelineCanvas>("TimelineKeys")!; var ruler = view.FindControl<TimelineCanvas>("TimelineRuler")!;
                var xRow = session.VisibleNodes.ToList().FindIndex(node => node.Value?.Property == "transform.x"); var yRow = session.VisibleNodes.ToList().FindIndex(node => node.Value?.Property == "transform.y");
                var input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.DataContext is TimelineValueRow row && row.Property == "transform.x");
                var inputCenter = input.TranslatePoint(new Point(0, input.Bounds.Height / 2), window)!.Value.Y;
                Assert.That(inputCenter, Is.EqualTo(CanvasPoint(window, canvas, xRow, 10).Y).Within(1), "Tree leaf and key diamonds share the same physical row.");
                var root = session.Tree[0]; var toggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(control => control.Name == "TimelineExpand" && ReferenceEquals(control.DataContext, root)); TimelineClick(window, toggle);
                Assert.That(session.Canvas.Rows, Has.Count.EqualTo(1)); Assert.That(session.Canvas.Rows[0].IsSummary, Is.True); Assert.That(session.Canvas.Rows[0].Keys, Has.Count.EqualTo(6));
                var summary = CanvasPoint(window, canvas, 0, 10); window.MouseDown(summary, MouseButton.Left); window.MouseMove(summary + new Vector(16, 0)); window.MouseUp(summary + new Vector(16, 0), MouseButton.Left);
                Assert.That(session.Canvas.Selection, Has.Count.EqualTo(2)); Assert.That(session.Canvas.HasPendingMove, Is.False); Assert.That(history.CanUndo, Is.False, "Parent summary diamonds only select, never drag child keys.");
                toggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(control => control.Name == "TimelineExpand" && ReferenceEquals(control.DataContext, root)); TimelineClick(window, toggle);
                window.Height = 320; TimelinePump(window); var vertical = view.FindControl<ScrollViewer>("TimelineVertical")!;
                Assert.That(vertical.Extent.Height, Is.GreaterThan(vertical.Viewport.Height)); vertical.Offset = new Vector(0, 38); TimelinePump(window);
                input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.DataContext is TimelineValueRow row && row.Property == "transform.x");
                Assert.That(input.TranslatePoint(new Point(0, input.Bounds.Height / 2), window)!.Value.Y, Is.EqualTo(CanvasPoint(window, canvas, xRow, 10).Y).Within(1));
                window.Height = 900; vertical.Offset = default; TimelinePump(window);
                view.FindControl<Slider>("TimelineZoom")!.Value = 12; view.FindControl<ScrollBar>("TimelineScroll")!.Value = 3; TimelinePump(window);
                Assert.That(session.Canvas.StartFrame, Is.EqualTo(3)); Assert.That(session.Canvas.PixelsPerFrame, Is.EqualTo(12));
                var seek = ruler.TranslatePoint(new Point(84, 12), window)!.Value; window.MouseDown(seek, MouseButton.Left); window.MouseUp(seek, MouseButton.Left); TimelinePump(window);
                Assert.That(session.Frame, Is.EqualTo(10)); Assert.That(history.CanUndo, Is.False);
                session.Canvas.StartFrame = 0; session.Canvas.PixelsPerFrame = 8; TimelinePump(window);
                var first = CanvasPoint(window, canvas, xRow, 10); var second = CanvasPoint(window, canvas, yRow, 10);
                window.MouseDown(first, MouseButton.Left); window.MouseUp(first, MouseButton.Left);
                window.MouseDown(second, MouseButton.Left, RawInputModifiers.Control); window.MouseUp(second, MouseButton.Left, RawInputModifiers.Control);
                Assert.That(session.Canvas.Selection, Has.Count.EqualTo(2)); var before = entry.Source.Parameters["timeline"].GetRawText();
                window.MouseDown(first, MouseButton.Left);
                for (var step = 1; step <= 8; step++) window.MouseMove(first + new Vector(step * 2, 0));
                Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(before), "Pointer updates only preview a local gesture.");
                window.MouseUp(first + new Vector(16, 0), MouseButton.Left); TimelinePump(window);
                Assert.That(entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys")[1].GetProperty("Frame").GetInt32(), Is.EqualTo(12));
                Assert.That(entry.Source.Parameters["timeline"].GetProperty("Tracks")[1].GetProperty("Keys")[1].GetProperty("Frame").GetInt32(), Is.EqualTo(12));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); history.Clear(); TimelinePump(window);
                first = CanvasPoint(window, canvas, xRow, 12); window.MouseDown(first, MouseButton.Left); window.MouseMove(first + new Vector(64, 0)); window.MouseUp(first + new Vector(64, 0), MouseButton.Left); TimelinePump(window);
                Assert.That(session.Canvas.HasPendingMove, Is.True); Assert.That(session.Canvas.IsMoveValid, Is.False); Assert.That(history.CanUndo, Is.False);
                PressGroupKey(canvas, Key.Escape); Assert.That(session.Canvas.HasPendingMove, Is.False); Assert.That(history.CanUndo, Is.False);
                window.MouseDown(first, MouseButton.Left); window.MouseMove(first + new Vector(64, 0)); window.MouseUp(first + new Vector(64, 0), MouseButton.Left); TimelinePump(window);
                vm.ReplaceCollisionsCommand.Execute(null); TimelinePump(window);
                var keys = entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys"); Assert.That(keys.GetArrayLength(), Is.EqualTo(2)); Assert.That(keys[1].GetProperty("Frame").GetInt32(), Is.EqualTo(20)); Assert.That(keys[1].GetProperty("futureKey")[1].ValueKind, Is.EqualTo(JsonValueKind.Null));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                var zero = CanvasPoint(window, canvas, xRow, 0) + new Vector(1, 0); window.MouseDown(zero, MouseButton.Left); window.MouseUp(zero, MouseButton.Left); PressGroupKey(canvas, Key.Delete);
                Assert.That(history.CanUndo, Is.False); Assert.That(session.Message, Is.EqualTo(scope.GetRequiredService<IEditorLocalizationService>()["Timeline.KeyProtected"]));
                window.MouseDown(zero, MouseButton.Left); window.MouseMove(zero + new Vector(16, 0)); PressGroupKey(canvas, Key.Escape); window.MouseUp(zero + new Vector(16, 0), MouseButton.Left); Assert.That(history.CanUndo, Is.False); Assert.That(session.Canvas.HasPendingMove, Is.False);
                var boxStart = CanvasPoint(window, canvas, xRow, 8) - new Vector(0, 15); var boxEnd = CanvasPoint(window, canvas, yRow, 14) + new Vector(0, 15);
                window.MouseDown(boxStart, MouseButton.Left); window.MouseMove(boxEnd); window.MouseUp(boxEnd, MouseButton.Left); Assert.That(session.Canvas.Selection, Has.Count.EqualTo(2)); Assert.That(history.CanUndo, Is.False);
                first = CanvasPoint(window, canvas, xRow, 12); window.MouseDown(first, MouseButton.Left); window.MouseMove(first + new Vector(16, 0));
                var changed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(entry.Source.Parameters["timeline"].GetRawText())!; changed["external"] = JsonSerializer.SerializeToElement(true);
                var parameters = new Dictionary<string, JsonElement>(entry.Source.Parameters) { ["timeline"] = JsonSerializer.SerializeToElement(changed) }; scope.GetRequiredService<EntryFieldAuthoringService>().SetParameters(group.Id, entry.Source, entry.Type, parameters);
                window.MouseUp(first + new Vector(16, 0), MouseButton.Left); Assert.That(session.Canvas.HasPendingMove, Is.False); Assert.That(session.Canvas.Selection, Is.Empty);
                Assert.That(entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys")[1].GetProperty("Frame").GetInt32(), Is.EqualTo(12));
                var localization = scope.GetRequiredService<IEditorLocalizationService>();
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                { localization.ApplyLocale(locale); TimelinePump(window); Assert.That(view.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.AddKeyCommand)).Content, Is.EqualTo(localization["Timeline.AddKey"])); }
                TimelinePump(window); if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "p6e2-key-canvas.png")); }
                await workspace.SaveAsync(); var path = project.RootPath; var groupId = group.Id; var entryId = entry.StableId; window.Close(); page.Dispose(); await projects.CloseAsync();
                project = await projects.OpenAsync(path); var saved = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == entryId).Source.Parameters["timeline"];
                Assert.That(saved.GetProperty("opaque").GetProperty("big").GetRawText(), Is.EqualTo("9007199254740993")); Assert.That(saved.GetProperty("external").GetBoolean(), Is.True); await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TimelineTimingRetimeTrimAndFrameInsertionAreAtomicAndPreserveUnknownContentAndEventOrder()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "TimelineTiming"), "Timing", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); entry.Source.Parameters["timeline"] = KeyCanvasFixture();
        using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
        var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear(); var original = entry.Source.Parameters["timeline"].GetRawText();
        foreach (var invalid in new (decimal? Duration, decimal? Fps)[] { (-1, 60), (60, 0), (60, 241), (60.5m, 60), (60, null), (null, 60) })
        { session.TimingDurationDraft = invalid.Duration; session.TimingFrameRateDraft = invalid.Fps; session.ApplyTiming(false); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original)); }
        session.TimingFrameRateDraft = 60;
        session.TimingDurationDraft = 15; session.ApplyTiming(false); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original));
        session.ApplyTiming(true); var root = entry.Source.Parameters["timeline"]; Assert.That(root.GetProperty("DurationFrames").GetInt32(), Is.EqualTo(15)); Assert.That(root.GetProperty("Tracks")[0].GetProperty("Keys").GetArrayLength(), Is.EqualTo(2)); Assert.That(root.GetProperty("Events").EnumerateArray().Select(item => item.GetProperty("tag").GetString()), Is.EqualTo(new[] { "a", "b" }));
        await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); history.Clear();
        session.TimingFrameRateDraft = 120; session.ApplyTiming(false); root = entry.Source.Parameters["timeline"]; Assert.That(root.GetProperty("FrameRate").GetInt32(), Is.EqualTo(120)); Assert.That(root.GetProperty("Tracks")[0].GetProperty("Keys")[1].GetProperty("Frame").GetInt32(), Is.EqualTo(10));
        await history.UndoAsync(); history.Clear(); session.KeepSeconds = true; session.TimingFrameRateDraft = 120; session.ApplyTiming(false); root = entry.Source.Parameters["timeline"];
        Assert.That(root.GetProperty("DurationFrames").GetInt32(), Is.EqualTo(120)); var key = root.GetProperty("Tracks")[0].GetProperty("Keys")[1]; Assert.That(key.GetProperty("Frame").GetInt32(), Is.EqualTo(20)); Assert.That(key.GetProperty("InTangent").GetSingle(), Is.EqualTo(.5f)); Assert.That(key.GetProperty("OutTangent").GetSingle(), Is.EqualTo(1));
        Assert.That(root.GetProperty("Events")[0].GetProperty("Frame").GetInt32(), Is.EqualTo(24)); Assert.That(root.GetProperty("Events")[1].GetProperty("tag").GetString(), Is.EqualTo("b")); Assert.That(root.GetProperty("opaque").GetProperty("big").GetRawText(), Is.EqualTo("9007199254740993"));
        await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); history.Clear(); session.TimingFrameRateDraft = 1; session.ApplyTiming(false); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original));
        session.KeepSeconds = false; session.Frame = 5; session.SetActiveTrack(session.Rows.Single(row => row.Property == "transform.x")); session.AddCurrentKey(); root = entry.Source.Parameters["timeline"]; Assert.That(root.GetProperty("Tracks")[0].GetProperty("Keys")[1].GetProperty("frame").GetInt32(), Is.EqualTo(5)); Assert.That(session.Canvas.Selection, Has.Count.EqualTo(1));
        session.AddCurrentKey(); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False, "Adding at an existing frame only selects that key."); history.Clear();
        session.Frame = 5; session.NavigateKey(1); Assert.That(session.Frame, Is.EqualTo(10)); session.NavigateKey(-1); Assert.That(session.Frame, Is.EqualTo(0)); Assert.That(history.CanUndo, Is.False);
        await projects.CloseAsync();
    }
}
