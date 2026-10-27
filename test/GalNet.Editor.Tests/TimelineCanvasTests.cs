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
        new Point((frame - canvas.Model!.StartFrame) * canvas.Model.PixelsPerFrame, canvas.Model.RowTop(row) + canvas.Model.RowHeight / 2), window)!.Value;

    [Test]
    public async Task TimelineCanvasRealPointerGesturesAlignSelectMoveCancelReplaceProtectAndRejectStaleTargets()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
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
                foreach (var check in view.GetVisualDescendants().OfType<CheckBox>())
                { Assert.That(check.VerticalAlignment, Is.EqualTo(Avalonia.Layout.VerticalAlignment.Center)); Assert.That(check.VerticalContentAlignment, Is.EqualTo(Avalonia.Layout.VerticalAlignment.Center)); }
                var spins = view.GetVisualDescendants().OfType<RepeatButton>().Where(button => button.GetVisualAncestors().OfType<NumericUpDown>().Any()).ToArray();
                Assert.That(spins.Length, Is.GreaterThanOrEqualTo(6), "Real NumericUpDown templates expose their repeat buttons.");
                foreach (var spin in spins) { Assert.That(spin.Delay, Is.EqualTo(250), string.Join(" / ", spin.GetVisualAncestors().Prepend(spin).OfType<Control>().Select(control => $"{control.GetType().FullName}#{control.Name} template={control.TemplatedParent?.GetType().FullName}"))); Assert.That(spin.Interval, Is.EqualTo(30)); }
                var mode = view.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "TimelineBlendMode" && control.DataContext is TimelineValueRow row && row.Property == "transform.x");
                Assert.That(mode.IsVisible, Is.True); mode.Focus(); PressGroupKey(mode, Key.Down); TimelinePump(window);
                Assert.That(entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("blendMode").GetString(), Is.EqualTo("Additive"));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                var durationInput = view.FindControl<NumericUpDown>("TimelineDurationDraft")!; var durationText = durationInput.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.That(durationInput.Minimum, Is.EqualTo(59)); durationText.Focus(); durationText.Text = "80"; TimelinePump(window); PressGroupKey(durationText, Key.Enter); TimelinePump(window);
                Assert.That(session.Duration, Is.EqualTo(80)); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                durationText.Focus(); durationText.Text = "90"; TimelinePump(window); PressGroupKey(durationText, Key.Escape); TimelinePump(window);
                Assert.That(session.TimingDurationDraft, Is.EqualTo(60)); Assert.That(session.Duration, Is.EqualTo(60)); Assert.That(history.CanUndo, Is.False);
                var fpsInput = view.FindControl<NumericUpDown>("TimelineFpsDraft")!; var fpsText = fpsInput.GetVisualDescendants().OfType<TextBox>().Single();
                fpsText.Focus(); fpsText.Text = "30"; TimelinePump(window); canvas.Focus(); TimelinePump(window);
                Assert.That(session.FrameRate, Is.EqualTo(30)); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                var xRow = session.VisibleNodes.ToList().FindIndex(node => node.Value?.Property == "transform.x"); var yRow = session.VisibleNodes.ToList().FindIndex(node => node.Value?.Property == "transform.y");
                var input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.DataContext is TimelineValueRow row && row.Property == "transform.x");
                var inputCenter = input.TranslatePoint(new Point(0, input.Bounds.Height / 2), window)!.Value.Y;
                Assert.That(inputCenter, Is.EqualTo(CanvasPoint(window, canvas, xRow, 10).Y).Within(1), "Tree leaf and key diamonds share the same physical row.");
                session.Canvas.Select([session.Canvas.Rows[xRow].Keys.Single(key => key.Frame == 10).Id]);
                var root = session.Tree[0]; var toggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(control => control.Name == "TimelineExpand" && ReferenceEquals(control.DataContext, root)); TimelineClick(window, toggle);
                Assert.That(session.Canvas.Rows, Has.Count.EqualTo(1)); Assert.That(session.Canvas.Rows[0].IsSummary, Is.True); Assert.That(session.Canvas.Rows[0].Keys, Has.Count.EqualTo(6));
                var summary = CanvasPoint(window, canvas, 0, 10); window.MouseDown(summary, MouseButton.Left, RawInputModifiers.Control); window.MouseMove(summary + new Vector(16, 0)); window.MouseUp(summary + new Vector(16, 0), MouseButton.Left, RawInputModifiers.Control);
                Assert.That(session.Canvas.Selection, Has.Count.EqualTo(2)); Assert.That(session.Canvas.HasPendingMove, Is.False); Assert.That(history.CanUndo, Is.True, "A collapsed parent moves every child key represented by the summary diamond.");
                var parentMove = entry.Source.Parameters["timeline"].GetProperty("Tracks");
                Assert.That(parentMove.EnumerateArray().Select(track => track.GetProperty("Keys")[1].GetProperty("Frame").GetInt32()), Is.EqualTo(new[] { 12, 12 }));
                Assert.That(parentMove.EnumerateArray().Select(track => track.GetProperty("Keys")[2].GetProperty("Frame").GetInt32()), Is.EqualTo(new[] { 20, 20 }));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); await history.UndoAsync(); TimelinePump(window);
                root = session.Tree[0];
                toggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(control => control.Name == "TimelineExpand" && ReferenceEquals(control.DataContext, root)); TimelineClick(window, toggle);
                var propertyGroup = session.Tree[0].Children.Single(node => node.Children.Any(child => child.Value?.Property == "transform.x"));
                propertyGroup.IsExpanded = false; TimelinePump(window);
                var groupRow = session.VisibleNodes.IndexOf(propertyGroup); var groupPoint = CanvasPoint(window, canvas, groupRow, 10);
                window.MouseDown(groupPoint, MouseButton.Left); window.MouseMove(groupPoint + new Vector(16, 0));
                Assert.That(history.CanUndo, Is.False, "Parent gestures preview until release.");
                window.MouseUp(groupPoint + new Vector(16, 0), MouseButton.Left); TimelinePump(window);
                Assert.That(entry.Source.Parameters["timeline"].GetProperty("Tracks").EnumerateArray().Select(track => track.GetProperty("Keys")[1].GetProperty("Frame").GetInt32()), Is.EqualTo(new[] { 12, 12 }));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); TimelinePump(window);
                propertyGroup = session.Tree[0].Children.Single(node => node.Children.Any(child => child.Value?.Property == "transform.x"));
                groupRow = session.VisibleNodes.IndexOf(propertyGroup); groupPoint = CanvasPoint(window, canvas, groupRow, 10);
                window.MouseDown(groupPoint, MouseButton.Left); window.MouseMove(groupPoint + new Vector(80, 0)); window.MouseUp(groupPoint + new Vector(80, 0), MouseButton.Left);
                Assert.That(session.Canvas.HasPendingMove, Is.True); Assert.That(session.Canvas.IsMoveValid, Is.False); Assert.That(history.CanUndo, Is.False);
                PressGroupKey(canvas, Key.Escape); Assert.That(session.Canvas.HasPendingMove, Is.False);
                propertyGroup.IsExpanded = true; TimelinePump(window);
                window.Height = 320; TimelinePump(window); var vertical = view.FindControl<ScrollViewer>("TimelineVertical")!;
                Assert.That(vertical.Extent.Height, Is.GreaterThan(vertical.Viewport.Height)); vertical.Offset = new Vector(0, 38); TimelinePump(window);
                input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.DataContext is TimelineValueRow row && row.Property == "transform.x");
                Assert.That(input.TranslatePoint(new Point(0, input.Bounds.Height / 2), window)!.Value.Y, Is.EqualTo(CanvasPoint(window, canvas, xRow, 10).Y).Within(1));
                window.Height = 900; vertical.Offset = default; TimelinePump(window);
                view.FindControl<Slider>("TimelineZoom")!.Value = 12; view.FindControl<ScrollBar>("TimelineScroll")!.Value = 3; TimelinePump(window);
                Assert.That(session.Canvas.StartFrame, Is.EqualTo(3)); Assert.That(session.Canvas.PixelsPerFrame, Is.EqualTo(12));
                var seek = ruler.TranslatePoint(new Point(84, 12), window)!.Value; window.MouseDown(seek, MouseButton.Left); window.MouseUp(seek, MouseButton.Left); TimelinePump(window);
                Assert.That(session.Frame, Is.EqualTo(10)); Assert.That(history.CanUndo, Is.False);
                var originalRow = session.Rows[0]; var seeks = 0; void CountSeek(int _) => seeks++;
                session.Canvas.SeekRequested += CountSeek;
                var endSeek = ruler.TranslatePoint(new Point(204, 12), window)!.Value;
                window.MouseDown(seek, MouseButton.Left);
                for (var step = 1; step <= 12; step++) window.MouseMove(seek + new Vector(step * 10, 0));
                Assert.That(session.Frame, Is.EqualTo(10)); Assert.That(session.Canvas.DisplayFrame, Is.EqualTo(20));
                Assert.That(session.Rows[0], Is.SameAs(originalRow), "Scrubbing only paints the preview, without rebuilding property rows."); Assert.That(seeks, Is.Zero);
                window.MouseUp(endSeek, MouseButton.Left); TimelinePump(window);
                Assert.That(session.Frame, Is.EqualTo(20)); Assert.That(seeks, Is.EqualTo(1)); Assert.That(history.CanUndo, Is.False);
                window.MouseDown(seek, MouseButton.Left); window.MouseMove(endSeek); PressGroupKey(ruler, Key.Escape); window.MouseUp(seek, MouseButton.Left);
                Assert.That(session.Frame, Is.EqualTo(20)); Assert.That(session.Canvas.DisplayFrame, Is.EqualTo(20)); Assert.That(seeks, Is.EqualTo(1));
                window.MouseDown(seek, MouseButton.Left); window.MouseMove(endSeek); session.Canvas.Invalidate(); window.MouseUp(seek, MouseButton.Left);
                Assert.That(session.Frame, Is.EqualTo(20)); Assert.That(seeks, Is.EqualTo(1), "Invalidated scrubbing cannot seek a replacement target.");
                session.Canvas.SeekRequested -= CountSeek; session.Frame = 10;
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
                first = CanvasPoint(window, canvas, xRow, 12); window.MouseDown(first, MouseButton.Left); window.MouseMove(first + new Vector(480, 0));
                Assert.That(session.Duration, Is.EqualTo(60), "Dragging past the end only previews until release.");
                window.MouseUp(first + new Vector(480, 0), MouseButton.Left); TimelinePump(window);
                Assert.That(session.Duration, Is.EqualTo(72)); Assert.That(entry.Source.Parameters["timeline"].GetProperty("Tracks")[0].GetProperty("Keys")[2].GetProperty("Frame").GetInt32(), Is.EqualTo(72));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False, "Duration extension and all selected keys are a single edit."); Assert.That(session.Duration, Is.EqualTo(60)); TimelinePump(window);
                first = CanvasPoint(window, canvas, xRow, 12); window.MouseDown(first, MouseButton.Left); window.MouseMove(first + new Vector(480, 0)); PressGroupKey(canvas, Key.Escape); window.MouseUp(first + new Vector(480, 0), MouseButton.Left);
                Assert.That(session.Duration, Is.EqualTo(60)); Assert.That(history.CanUndo, Is.False);
                session.Canvas.Select([session.Canvas.Rows[xRow].Keys.Single(key => key.Frame == 12).Id]); Assert.That(session.Canvas.StartMove(), Is.True); session.Canvas.UpdateMove(int.MaxValue);
                Assert.That(session.Canvas.IsMoveValid, Is.False); session.Canvas.Cancel(); Assert.That(history.CanUndo, Is.False);
                first = CanvasPoint(window, canvas, xRow, 12); window.MouseDown(first, MouseButton.Left); window.MouseMove(first + new Vector(64, 0)); window.MouseUp(first + new Vector(64, 0), MouseButton.Left); TimelinePump(window);
                Assert.That(session.Canvas.HasPendingMove, Is.True); Assert.That(session.Canvas.IsMoveValid, Is.False); Assert.That(history.CanUndo, Is.False);
                PressGroupKey(canvas, Key.Escape); Assert.That(session.Canvas.HasPendingMove, Is.False); Assert.That(history.CanUndo, Is.False);
                window.MouseDown(first, MouseButton.Left); window.MouseMove(first + new Vector(64, 0)); window.MouseUp(first + new Vector(64, 0), MouseButton.Left); TimelinePump(window);
                session.ReplaceKeyCollisions(); TimelinePump(window);
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
                {
                    localization.ApplyLocale(locale); TimelinePump(window);
                    var addKey = view.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.AddKeyCommand));
                    Assert.That(addKey.Content, Is.TypeOf<PathIcon>());
                    Assert.That(ToolTip.GetTip(addKey), Is.EqualTo(localization["Timeline.AddKey"]));
                    Assert.That(Avalonia.Automation.AutomationProperties.GetName(addKey), Is.EqualTo(localization["Timeline.AddKey"]));
                    mode = view.GetVisualDescendants().OfType<ComboBox>().First(control => control.Name == "TimelineBlendMode");
                    Assert.That(mode.Items.OfType<ComboBoxItem>().Select(item => item.Content), Is.EqualTo(new[] { localization["Timeline.Absolute"], localization["Timeline.Offset"] }));
                }
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
    public async Task TimelineTimingFieldsCommitIndependentlyClampToContentAndPreserveFramesAndJson()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "TimelineTiming"), "Timing", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GalNet.Editor.Models.Graph.GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); entry.Source.Parameters["timeline"] = KeyCanvasFixture();
        using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
        var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear(); var original = entry.Source.Parameters["timeline"].GetRawText();
        foreach (var invalid in new decimal?[] { -1, .5m, null })
        { session.TimingDurationDraft = invalid; session.CommitTimingInput(false); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original)); }
        foreach (var invalid in new decimal?[] { 0, 241, 1.5m, null })
        { session.TimingFrameRateDraft = invalid; session.CommitTimingInput(true); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(original)); }
        Assert.That(session.MinimumDuration, Is.EqualTo(59));
        session.TimingDurationDraft = 15; session.CommitTimingInput(false);
        var root = entry.Source.Parameters["timeline"]; Assert.That(root.GetProperty("DurationFrames").GetInt32(), Is.EqualTo(59)); Assert.That(session.TimingDurationDraft, Is.EqualTo(59));
        Assert.That(JsonElement.DeepEquals(root.GetProperty("Tracks"), KeyCanvasFixture().GetProperty("Tracks")), Is.True);
        Assert.That(JsonElement.DeepEquals(root.GetProperty("Events"), KeyCanvasFixture().GetProperty("Events")), Is.True);
        await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); history.Clear();
        session.TimingDurationDraft = null; session.TimingFrameRateDraft = 120; session.CommitTimingInput(true); root = entry.Source.Parameters["timeline"];
        Assert.That(root.GetProperty("FrameRate").GetInt32(), Is.EqualTo(120)); Assert.That(root.GetProperty("DurationFrames").GetInt32(), Is.EqualTo(60));
        Assert.That(JsonElement.DeepEquals(root.GetProperty("Tracks"), KeyCanvasFixture().GetProperty("Tracks")), Is.True); Assert.That(JsonElement.DeepEquals(root.GetProperty("Events"), KeyCanvasFixture().GetProperty("Events")), Is.True);
        Assert.That(root.GetProperty("opaque").GetProperty("big").GetRawText(), Is.EqualTo("9007199254740993"));
        session.CommitTimingInput(true); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False, "Reconfirming the same FPS adds no history."); history.Clear();
        var mode = session.Rows[0]; mode.BlendModeIndex = 1; root = entry.Source.Parameters["timeline"]; Assert.That(root.GetProperty("Tracks")[0].GetProperty("blendMode").GetString(), Is.EqualTo("Additive"));
        Assert.That(JsonElement.DeepEquals(root.GetProperty("Tracks")[0].GetProperty("Keys"), KeyCanvasFixture().GetProperty("Tracks")[0].GetProperty("Keys")), Is.True);
        await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); mode.BlendModeIndex = 0; Assert.That(history.CanUndo, Is.False, "Old rows cannot change a restored track.");
        session.Rows[0].BlendModeIndex = 1; history.Clear();
        session.Frame = 5; session.SetActiveTrack(session.Rows.Single(row => row.Property == "transform.x")); session.AddCurrentKey(); root = entry.Source.Parameters["timeline"]; Assert.That(root.GetProperty("Tracks")[0].GetProperty("Keys")[1].GetProperty("frame").GetInt32(), Is.EqualTo(5)); Assert.That(session.Canvas.Selection, Has.Count.EqualTo(1));
        session.AddCurrentKey(); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False, "Adding at an existing frame only selects that key."); history.Clear();
        session.Frame = 5; session.NavigateKey(1); Assert.That(session.Frame, Is.EqualTo(10)); session.NavigateKey(-1); Assert.That(session.Frame, Is.EqualTo(0)); Assert.That(history.CanUndo, Is.False);
        var parameters = new Dictionary<string, JsonElement>(entry.Source.Parameters) { ["timeline"] = JsonSerializer.Deserialize<JsonElement>("{\"tracks\":[],\"events\":[],\"unknown\":true}") };
        scope.GetRequiredService<EntryFieldAuthoringService>().SetParameters(group.Id, entry.Source, entry.Type, parameters); history.Clear(); var omitted = entry.Source.Parameters["timeline"].GetRawText();
        session.CommitTimingInput(false); session.CommitTimingInput(true); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["timeline"].GetRawText(), Is.EqualTo(omitted), "Confirming unchanged defaults does not materialize omitted fields.");
        await projects.CloseAsync();
    }
}
