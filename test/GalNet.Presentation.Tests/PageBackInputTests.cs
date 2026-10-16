using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.Themes;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class PageBackInputTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task EscapeUsesPageCommandOncePerPressEvenAfterPageChanges(bool buttonFocus) => Ui(() =>
    {
        var count = 0;
        var first = new ReturnPage(() => count++);
        var second = new ReturnPage(() => count++);
        BackNavigablePageViewModel current = first;
        var button = new Button();
        var root = new Border { Child = button };
        var window = new Window { Content = root };
        using var listener = new PageBackInputListener(root, () => current);
        window.Show();
        try
        {
            if (buttonFocus) button.Focus(); else window.Focus();
            Press(window);
            Assert.That(count, Is.EqualTo(1));
            current = second;
            Press(window);
            Assert.That(count, Is.EqualTo(1), "Holding Escape must not pop multiple pages.");
            Release(window);
            Stroke(window);
            Assert.That(count, Is.EqualTo(2));
            second.BackCommand.Execute(null);
            Assert.That(count, Is.EqualTo(3), "The button and keyboard must use the same override.");
        }
        finally { window.Close(); }
    });

    [Test]
    public Task CaptureCancellationAndRepeatedEscapeDoNotLeaveThePage() => Ui(() =>
    {
        using var settings = new PlayerInputSettings();
        var count = 0;
        var page = new ReturnPage(() => count++);
        var capture = new InputBindingCapture(settings, "toggle.auto", PlayerInputDevice.KeyboardMouse, 0);
        var root = new Border { Child = capture };
        var window = new Window { Content = root };
        using var listener = new PageBackInputListener(root, () => page, () => settings.IsCapturing);
        window.Show();
        try
        {
            capture.Focus();
            capture.BeginCapture();
            Press(window);
            Assert.That(settings.IsCapturing, Is.False);
            Assert.That(count, Is.Zero);
            Press(window);
            Assert.That(count, Is.Zero);
            Release(window);
            Stroke(window);
            Assert.That(count, Is.EqualTo(1));
        }
        finally { window.Close(); }
    });

    [Test]
    public Task LocalCancellationAndDropDownGetEscapeBeforeNavigation() => Ui(() =>
    {
        var count = 0;
        var page = new ReturnPage(() => count++);
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
        var root = new Border { Classes = { "galnet-player" }, Child = combo };
        var window = new Window { Content = root };
        window.Styles.Add(new ClassicTheme().CreateStyles());
        using var listener = new PageBackInputListener(root, () => page);
        window.Show();
        try
        {
            combo.Focus();
            window.UpdateLayout();
            combo.IsDropDownOpen = true;
            Stroke(window);
            Assert.That(combo.IsDropDownOpen, Is.False);
            Assert.That(count, Is.Zero);
            EventHandler<KeyEventArgs> cancel = (_, e) => { if (e.Key == Key.Escape) e.Handled = true; };
            combo.AddHandler(InputElement.KeyDownEvent, cancel, RoutingStrategies.Bubble);
            Stroke(window);
            Assert.That(count, Is.Zero);
            combo.RemoveHandler(InputElement.KeyDownEvent, cancel);
            Stroke(window);
            Assert.That(count, Is.EqualTo(1));
        }
        finally { window.Close(); }
    });

    [Test]
    public Task ListenerIsScopedAndFollowsReattachmentWithoutLeakingAfterDispose() => Ui(() =>
    {
        var count = 0;
        var page = new ReturnPage(() => count++);
        BackNavigablePageViewModel? current = page;
        var root = new Border { Focusable = true };
        var hostButton = new Button { MinHeight = 40, MinWidth = 80 };
        var host = new StackPanel { Children = { root, hostButton } };
        var first = new Window { Content = host };
        var second = new Window();
        using var listener = new PageBackInputListener(root, () => current);
        first.Show();
        try
        {
            Assert.That(hostButton.Focus(), Is.True);
            Stroke(first);
            Assert.That(count, Is.Zero);
            first.Focus();
            current = null;
            Stroke(first);
            current = page;
            root.IsVisible = false;
            Stroke(first);
            Assert.That(count, Is.Zero);
            root.IsVisible = true;
            root.Focus();
            Stroke(first);
            Assert.That(count, Is.EqualTo(1));
            host.Children.Remove(root);
            first.Focus();
            Stroke(first);
            Assert.That(count, Is.EqualTo(1));
            second.Content = root;
            second.Show();
            root.Focus();
            Stroke(second);
            Assert.That(count, Is.EqualTo(2));
            listener.Dispose();
            Stroke(second);
            Assert.That(count, Is.EqualTo(2));
        }
        finally { first.Close(); second.Close(); }
    });

    private sealed class ReturnPage(Action back) : BackNavigablePageViewModel(null!)
    {
        protected override void OnBack() => back();
    }
    private static void Press(Window window) => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    private static void Release(Window window) => window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    private static void Stroke(Window window) { Press(window); Release(window); }
    private static Task Ui(Action action) => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly)
        .Dispatch(() => { action(); Dispatcher.UIThread.RunJobs(); return true; }, TestContext.CurrentContext.CancellationToken);
}
