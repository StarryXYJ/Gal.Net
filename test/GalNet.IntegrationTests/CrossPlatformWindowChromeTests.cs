using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GeneralTest.Integration;
using Ursa.Controls;
#if GALNET_GAME_DEBUG
using GalNet.Avalonia.Debugging;
using GalNet.Avalonia.Debugging.Views;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.MarkupExtensions;
using Microsoft.Extensions.DependencyInjection;
#endif

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class CrossPlatformWindowChromeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MainWindowUsesUrsaAndThePlayerCaptionPalette(bool darkHost)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ScreenshotTestApplication).Assembly);
        await session.Dispatch<bool>(() =>
        {
            var semi = new Semi.Avalonia.SemiTheme();
            var ursa = new Ursa.Themes.Semi.UrsaSemiTheme();
            Application.Current!.Styles.Add(semi);
            Application.Current.Styles.Add(ursa);
            var window = new GalNet.Sample.CrossPlatform.Views.MainWindow
            {
                RequestedThemeVariant = darkHost ? ThemeVariant.Dark : ThemeVariant.Light
            };
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.That(window.GetType().BaseType, Is.EqualTo(typeof(UrsaWindow)));
                Assert.That(window.ExtendClientAreaToDecorationsHint, Is.True);
                Assert.That(window.WindowDecorations, Is.EqualTo(WindowDecorations.Full));
                Assert.That(window.WindowDecorationsTheme, Is.Not.Null);
                Assert.That(window.IsMinimizeButtonVisible && window.IsRestoreButtonVisible && window.IsCloseButtonVisible, Is.True);
                var titleBar = window.GetVisualDescendants().OfType<TitleBar>().Single();
                Assert.That(WindowDecorationProperties.GetElementRole(titleBar), Is.EqualTo(WindowDecorationsElementRole.TitleBar));
                var foreground = (ISolidColorBrush)window.FindResource("CaptionButtonForeground")!;
                var palette = (ISolidColorBrush)window.FindResource("GalNet.Brush.TextPrimary")!;
                Assert.That(foreground.Color, Is.EqualTo(palette.Color));
                var hover = (ISolidColorBrush)window.FindResource("CaptionButtonPointeroverBackground")!;
                Assert.That(hover.Color, Is.EqualTo(palette.Color));
                Assert.That(hover.Opacity, Is.EqualTo(.08));
                Assert.That(((ISolidColorBrush)window.FindResource("CaptionButtonClosePointeroverBackground")!).Color,
                    Is.EqualTo(Color.Parse("#C42B1C")));
            }
            finally
            {
                window.Close();
                Application.Current.Styles.Remove(ursa);
                Application.Current.Styles.Remove(semi);
            }

            return Task.FromResult(true);
        }, TestContext.CurrentContext.CancellationToken);
    }

#if GALNET_GAME_DEBUG
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task BothDesktopSamplesKeepDiagnosticsInTheUrsaTitleBarBeforeGameStartup(bool crossPlatform, bool darkHost)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ScreenshotTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var services = new ServiceCollection();
            GalNet.Sample.Avalonia.SampleCustomization.ConfigureServices(services);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            PlayerTextExtension.Resolve = scope.ServiceProvider.GetRequiredService<IPlayerLocalization>().GetString;
            var semi = new Semi.Avalonia.SemiTheme();
            var ursa = new Ursa.Themes.Semi.UrsaSemiTheme();
            Application.Current!.Styles.Add(semi);
            Application.Current.Styles.Add(ursa);
            UrsaWindow window;
            if (crossPlatform)
            {
                var desktop = new GalNet.Sample.CrossPlatform.Views.MainWindow();
                desktop.AttachDebugServices(scope.ServiceProvider);
                window = desktop;
            }
            else
            {
                var desktop = scope.ServiceProvider.GetRequiredService<GalNet.Sample.Avalonia.Views.MainWindow>();
                desktop.CompleteStartup("Game directory was not found.");
                window = desktop;
            }
            window.RequestedThemeVariant = darkHost ? ThemeVariant.Dark : ThemeVariant.Light;
            try
            {
                window.Show();
                window.UpdateLayout();
                var model = scope.ServiceProvider.GetRequiredService<GameDebugViewModel>();
                var menu = (DebugMenuView)window.LeftContent!;
                Assert.That(menu.IsVisible && menu.IsEnabled, Is.True, "Startup errors must not hide diagnostic logs.");
                Assert.That(model.IsGameReady, Is.False);
                Assert.That(menu.Bounds.Width, Is.GreaterThan(0).And.LessThan(window.ClientSize.Width / 2),
                    "The debug menu belongs in Ursa LeftContent, not a full-width game-content row.");
                Assert.That(((ISolidColorBrush)menu.Background!).Color,
                    Is.EqualTo(((ISolidColorBrush)window.FindResource("GalNet.Brush.Background")!).Color));
                var header = (MenuItem)menu.Items[0]!;
                AssertCaptionHighlight(window, header);
                var clear = (MenuItem)header.Items[0]!;
                var saves = (MenuItem)header.Items[3]!;
                Assert.That(clear.IsEnabled || saves.IsEnabled, Is.False);
                model.ShowLogCommand.Execute(null);
                var logs = window.OwnedWindows.OfType<DebugLogWindow>().Single();
                Assert.That(logs.IsVisible, Is.True);
                model.IsGameReady = true;
                window.UpdateLayout();
                Assert.That(clear.IsEnabled && saves.IsEnabled, Is.True);

                if (window is GalNet.Sample.CrossPlatform.Views.MainWindow desktop)
                {
                    desktop.DetachDebugServices();
                    Assert.That(logs.IsVisible, Is.False);
                    Assert.That(desktop.LeftContent, Is.Null);
                    model.ShowLogCommand.Execute(null);
                    Assert.That(desktop.OwnedWindows.OfType<DebugLogWindow>(), Is.Empty);
                    desktop.AttachDebugServices(scope.ServiceProvider);
                    model.ShowLogCommand.Execute(null);
                    Assert.That(desktop.OwnedWindows.OfType<DebugLogWindow>().ToArray(), Has.Length.EqualTo(1));
                }
                else logs.Close();
            }
            finally
            {
                window.Close();
                Application.Current.Styles.Remove(ursa);
                Application.Current.Styles.Remove(semi);
            }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    private static void AssertCaptionHighlight(Window window, MenuItem header)
    {
        var surface = header.GetVisualDescendants().OfType<Border>().First(control => control.Name == "PART_LayoutRoot");
        var text = header.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>()
            .First(control => control.Name == "PART_HeaderPresenter");
        var foreground = ((ISolidColorBrush)text.Foreground!).Color;
        var point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
        var away = new Point(window.ClientSize.Width / 2, window.ClientSize.Height / 2);
        window.MouseMove(point);
        window.UpdateLayout();
        AssertHighlight("CaptionButtonPointeroverBackground");
        window.MouseDown(point, MouseButton.Left);
        window.UpdateLayout();
        AssertHighlight("CaptionButtonPressedBackground");
        window.MouseUp(point, MouseButton.Left);
        header.IsSubMenuOpen = true;
        window.MouseMove(away);
        window.UpdateLayout();
        AssertHighlight("CaptionButtonPointeroverBackground");
        header.IsSubMenuOpen = false;
        header.IsSelected = false;
        window.UpdateLayout();

        void AssertHighlight(string captionResource)
        {
            var expected = (ISolidColorBrush)window.FindResource(captionResource)!;
            var actual = (ISolidColorBrush)surface.Background!;
            Assert.That(actual.Color, Is.EqualTo(expected.Color));
            Assert.That(actual.Opacity, Is.EqualTo(expected.Opacity));
            Assert.That(((ISolidColorBrush)text.Foreground!).Color, Is.EqualTo(foreground));
        }
    }
#endif
}
