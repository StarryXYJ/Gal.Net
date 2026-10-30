using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.MarkupExtensions;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Sample.Avalonia;
using GalNet.Sample.Avalonia.Views;
using GalNet.Sample.Player.Views;
using GeneralTest.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleWindowThemeTests
{
#if GALNET_GAME_DEBUG
    [Test]
    public async Task DebugMenuRendersItsHeaderAndPopupUsingTheHostMenuTheme()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ScreenshotTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var services = new ServiceCollection();
            SampleCustomization.ConfigureServices(services);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var localization = scope.ServiceProvider.GetRequiredService<IPlayerLocalization>();
            localization.CultureService.SetCulture("zh-CN");
            PlayerTextExtension.Resolve = localization.GetString;
            var semi = new Semi.Avalonia.SemiTheme();
            Application.Current!.Styles.Add(semi);
            var menu = new DebugMenuView
            {
                DataContext = scope.ServiceProvider.GetRequiredService<GalNet.Sample.Avalonia.Debug.SampleDebugViewModel>()
            };
            var window = new Window { Width = 640, Height = 400, Content = menu };
            window.Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.That(menu.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ItemsPresenter>(), Is.Not.Empty,
                    "The debug menu needs a real Menu template, not only IsVisible=true.");
                var header = (MenuItem)menu.Items[0]!;
                Assert.That(header.Header, Is.EqualTo("调试"));
                Assert.That(((ISolidColorBrush)header.Foreground!).Color,
                    Is.EqualTo(((ISolidColorBrush)window.FindResource("GalNet.Brush.TextPrimary")!).Color));
                Assert.That(header.Bounds.Width, Is.GreaterThan(0));
                Assert.That(header.Bounds.Height, Is.GreaterThan(0));
                header.IsSubMenuOpen = true;
                window.UpdateLayout();
                var popup = header.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Popup>().Single();
                Assert.That(popup.IsOpen, Is.True);
                Assert.That(popup.Child!.GetVisualDescendants().OfType<MenuItem>(), Is.Not.Empty);
            }
            finally { window.Close(); Application.Current.Styles.Remove(semi); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }
#endif

    [Test]
    public async Task CaptionResourcesAndExitConfirmationUseThePlayerPaletteAndKeepCancelKeyboardSafe()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ScreenshotTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var services = new ServiceCollection();
            SampleCustomization.ConfigureServices(services, enableDebug: false);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var localization = scope.ServiceProvider.GetRequiredService<IPlayerLocalization>();
            PlayerTextExtension.Resolve = localization.GetString;
            var semi = new Semi.Avalonia.SemiTheme();
            var ursa = new Ursa.Themes.Semi.UrsaSemiTheme();
            Application.Current!.Styles.Add(semi);
            Application.Current.Styles.Add(ursa);
            var window = scope.ServiceProvider.GetRequiredService<MainWindow>();
            window.Show();
            try
            {
                window.UpdateLayout();
                // Headless windows do not create platform decorations. Check their actual brush resources.
                var foreground = (ISolidColorBrush)window.FindResource("CaptionButtonForeground")!;
                var palette = (ISolidColorBrush)window.FindResource("GalNet.Brush.TextPrimary")!;
                Assert.That(foreground.Color, Is.EqualTo(palette.Color));
                var hover = (ISolidColorBrush)window.FindResource("CaptionButtonPointeroverBackground")!;
                Assert.That(hover.Color, Is.EqualTo(palette.Color));
                Assert.That(hover.Opacity, Is.EqualTo(.08));
                Assert.That(((ISolidColorBrush)window.FindResource("CaptionButtonClosePointeroverBackground")!).Color, Is.EqualTo(Color.Parse("#C42B1C")));
                var selection = window.SelectLanguageAsync("zh-CN", CancellationToken.None);
                window.UpdateLayout();
                var languageView = window.Player.FindControl<LanguageSelectionView>("LanguageSelection")!;
                var picker = languageView.FindControl<ComboBox>("LanguagePicker")!;
                Assert.That(((ISolidColorBrush)picker.Background!).Color,
                    Is.EqualTo(((ISolidColorBrush)languageView.FindResource("GalNet.Brush.Surface")!).Color));
                picker.IsDropDownOpen = true;
                window.UpdateLayout();
                var popup = picker.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Popup>().Single();
                Assert.That(((ISolidColorBrush)((Border)popup.Child!).Background!).Color,
                    Is.EqualTo(((ISolidColorBrush)picker.Background!).Color));
                Capture(window, "Sample-LanguagePicker");
                picker.IsDropDownOpen = false;
                scope.ServiceProvider.GetRequiredService<GalNet.Sample.Player.ViewModels.LanguageSelectionViewModel>().ConfirmCommand.Execute(null);
                Assert.That(await selection, Is.EqualTo("zh-CN"));
                scope.ServiceProvider.GetRequiredService<AvaloniaPlayerLocalization>().ApplyCulture("en-US");
                scope.ServiceProvider.GetRequiredService<IGameNavigationService>().ResetTo<TitlePageViewModel>(NavigationTransition.None);
                window.CompleteStartup();
                window.UpdateLayout();
                var title = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(control => control.Text == scope.ServiceProvider.GetRequiredService<TitlePageViewModel>().GameTitle);
                var titleBrush = (ISolidColorBrush)title.Foreground!;
                Capture(window, "Sample-TitleBeforeExit");
                var confirmation = window.ConfirmExitAsync();
                Assert.That(window.ConfirmExitAsync(), Is.SameAs(confirmation));
                var cancel = window.Player.FindControl<Button>("CancelExitButton")!;
                var content = window.Player.FindControl<Grid>("PlayerContent")!;
                Assert.That(content.IsEnabled, Is.False);
                Assert.That(cancel.IsFocused, Is.True);
                window.UpdateLayout();
                Capture(window, "Sample-ExitConfirmation");
                Assert.That(((ISolidColorBrush)title.Foreground!).Color, Is.EqualTo(titleBrush.Color), "The modal must preserve the title's foreground.");
                Assert.That(((ISolidColorBrush)title.Foreground!).Opacity, Is.EqualTo(titleBrush.Opacity), "The scrim alone dims static title text.");
                scope.ServiceProvider.GetRequiredService<AvaloniaPlayerLocalization>().ApplyCulture("zh-CN");
                window.UpdateLayout();
                Capture(window, "Sample-ExitConfirmation-zh-CN");
                window.Width = 800;
                window.Height = 500;
                window.UpdateLayout();
                Capture(window, "Sample-ExitConfirmation-zh-CN-compact");
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.That(await confirmation, Is.False);
                Assert.That(content.IsEnabled, Is.True);
                confirmation = window.ConfirmExitAsync();
                window.Player.FindControl<Button>("ConfirmExitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(await confirmation, Is.True);
            }
            finally { window.Close(); Application.Current.Styles.Remove(ursa); Application.Current.Styles.Remove(semi); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    private static void Capture(Window window, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var directory = Environment.GetEnvironmentVariable("GALNET_THEME_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        bitmap!.Save(Path.Combine(directory, name + ".png"));
    }
}
