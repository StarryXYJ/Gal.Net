using Avalonia.Controls;
using Avalonia.Headless;
using GalNet.Avalonia.GameView.Controls;
using GalNet.Contracts.Builtins.Scene;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class TextCardHostTests
{
    [Test]
    public Task Starting_a_replacement_emphasis_reads_the_current_value_without_rewriting_authored_keys() => OnUi(async () =>
    {
        var state = new GalNet.Avalonia.GameView.Presentation.GamePresentationState();
        double displayed = -.5;
        state.RegisterEffectAnimation("speaker", "exposure", value => displayed = value, -.5);
        using var presenter = new GalNet.Avalonia.GameView.Presentation.AvaloniaAnimationPresenter(state,
            GalNet.Avalonia.GameView.Presentation.AvaloniaUiDispatcher.Instance);
        var track = new TimelineTrackDefinition { HandleId = "speaker", Property = "exposure", StartFromCurrent = true,
            Keys = [new() { Frame = 0, Value = 0 }, new() { Frame = 60, Value = -1 }] };
        var playback = presenter.PlayTimelineAsync(new() { PlaybackHandleId = "emphasis", FrameRate = 60, DurationFrames = 60, Tracks = [track] }, CancellationToken.None);
        Assert.That(displayed, Is.InRange(-.6, -.45));
        presenter.CompleteAnimationImmediately("emphasis"); await playback;
        Assert.That(displayed, Is.EqualTo(-1)); Assert.That(track.Keys[0].Value, Is.Zero);
    });

    [Test]
    public Task Replacement_is_per_position_and_old_cancellation_does_not_remove_the_new_card() => OnUi(async () =>
    {
        var cards = new TextCardHost { EnterDuration = TimeSpan.Zero, ExitDuration = TimeSpan.Zero, HoldDuration = TimeSpan.FromSeconds(30) };
        var window = new Window { Content = cards }; window.Show();
        try
        {
            using var cancel = new CancellationTokenSource();
            var old = cards.ShowTextCardAsync(new("old", TextCardPosition.TopRight), cancel.Token);
            var other = cards.ShowTextCardAsync(new("other", TextCardPosition.BottomLeft), CancellationToken.None);
            var replacement = cards.ShowTextCardAsync(new("new", TextCardPosition.TopRight, "unknown-style"), CancellationToken.None);
            cancel.Cancel(); await old;
            Assert.That(cards.Children.OfType<ContentControl>().Select(c => ((Border)c.Content!).Child).OfType<TextBlock>().Select(c => c.Text), Is.EquivalentTo(new[] { "new", "other" }));
            cards.ClearTextCards(); await Task.WhenAll(other, replacement);
            Assert.That(cards.Children, Is.Empty);
        }
        finally { window.Close(); }
    });

    [Test]
    public Task Waiting_includes_entry_hold_and_exit_and_right_side_moves_from_the_right() => OnUi(async () =>
    {
        var cards = new TextCardHost { EnterDuration = TimeSpan.FromMilliseconds(50), ExitDuration = TimeSpan.FromMilliseconds(50), HoldDuration = TimeSpan.FromMilliseconds(50) };
        var window = new Window { Content = cards }; window.Show();
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var task = cards.ShowTextCardAsync(new("right", TextCardPosition.Right), CancellationToken.None);
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(cards.Children.Single().HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Right));
            await task;
            Assert.That(clock.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(120));
            Assert.That(cards.Children, Is.Empty);
        }
        finally { window.Close(); }
    });

    private static Task OnUi(Func<Task> action) => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly)
        .Dispatch(action, TestContext.CurrentContext.CancellationToken);
}
