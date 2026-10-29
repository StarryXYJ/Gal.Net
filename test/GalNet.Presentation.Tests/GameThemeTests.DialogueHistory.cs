using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Game.Controls;
using GalNet.Presentation.Abstractions.Application;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

public sealed partial class GameThemeTests
{
    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task HistoryOpensAtEndVirtualizesAndDoesNotAdvanceBehindTheOverlay(string layout)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, "Ocean");
            using var provider = CreateServices(theme).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var game = scope.ServiceProvider.GetRequiredService<GamePageViewModel>();
            var page = scope.ServiceProvider.GetRequiredService<GamePage>();
            var lines = Enumerable.Range(0, 2000).Select(index => new DialogueHistoryLine("main", $"line-{index}#2", "Alice",
                $"History line {index}: <b>bold</b> <color=#FF0000>red</color>", true, new("voice", "vocal"))).ToArray();
            game.GetHistoryLines = () => lines;
            game.IsGameUiReady = true;
            game.IsDialogueVisible = true;
            game.ReadTextBrush = Brushes.Cyan;
            var advances = 0;
            var replays = 0;
            var stops = 0;
            game.AdvanceRequested += () => advances++;
            game.StopVoiceReplayRequested += () => stops++;
            game.PlayVoiceAsync = _ => { replays++; return Task.CompletedTask; };
            game.SetCurrentDialogue(lines[^1], false);
            page.DataContext = game;
            var root = new Border { Child = page, Classes = { "galnet-player" } };
            root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = 1280, Height = 720, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                Assert.That(page.Dialogue.CanReplayVoice, Is.False);
                game.CompleteDialogueVoice(lines[^1]);
                window.UpdateLayout();
                Assert.That(page.Dialogue.CanReplayVoice, Is.True);
                var button = page.Dialogue.GetVisualDescendants().OfType<Button>().Single(control => control.Command == game.ReplayDialogueCommand);
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await game.ReplayDialogueCommand.ExecuteAsync(null);
                Assert.That(replays, Is.GreaterThanOrEqualTo(1));
                game.OpenHistoryCommand.Execute(null);
                window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                window.UpdateLayout();
                var history = (DialogueHistoryOverlay)page.History;
                Assert.That(history.IsEffectivelyVisible, Is.True);
                var list = history.FindControl<ItemsControl>("HistoryList")!;
                Assert.That(list.ContainerFromIndex(lines.Length - 1), Is.Not.Null, "Opening history must show its last line.");
                Assert.That(list.GetVisualDescendants().OfType<ListBoxItem>(), Is.Empty, "History rows must not be selectable.");
                Assert.That(list.GetRealizedContainers().Count(), Is.InRange(1, 99));
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.That(scroll.Offset.Y, Is.GreaterThan(0));
                game.AdvanceCommand.Execute(null);
                Assert.That(advances, Is.Zero);
                var rendered = list.GetVisualDescendants().OfType<TypewriterTextBlock>().First();
                var runs = rendered.Inlines!.OfType<Run>().ToArray();
                Assert.That(runs.Any(run => run.FontWeight == FontWeight.Bold), Is.True);
                Assert.That(runs.Any(run => run.Foreground is ISolidColorBrush brush && brush.Color == Colors.Red), Is.True);
                var row = list.ContainerFromIndex(lines.Length - 1)!;
                Assert.That(row.Focusable, Is.False);
                var text = row.GetVisualDescendants().OfType<TypewriterTextBlock>().Single();
                var point = text.TranslatePoint(new Point(10, text.Bounds.Height / 2), window)!.Value;
                var focused = window.FocusManager!.GetFocusedElement();
                var previousReplays = replays;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.That(window.FocusManager.GetFocusedElement(), Is.SameAs(focused));
                Assert.That(advances, Is.Zero);
                Assert.That(replays, Is.EqualTo(previousReplays));
                var replay = row.GetVisualDescendants().OfType<Button>().Single();
                var replayPoint = replay.TranslatePoint(new Point(replay.Bounds.Width / 2, replay.Bounds.Height / 2), window)!.Value;
                window.MouseDown(replayPoint, MouseButton.Left);
                window.MouseUp(replayPoint, MouseButton.Left);
                Assert.That(replays, Is.EqualTo(previousReplays + 1), "The separate replay button remains interactive.");
                Capture(window, $"{layout}-DialogueHistory");
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.That(game.IsHistoryOpen, Is.False);
                Assert.That(stops, Is.GreaterThan(0));
                game.AdvanceCommand.Execute(null);
                Assert.That(advances, Is.EqualTo(1));
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }
}
