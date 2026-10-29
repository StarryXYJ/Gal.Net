using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.MarkupExtensions;
using GalNet.Sample.Avalonia;
using GalNet.Sample.Avalonia.Views;
using GeneralTest.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleWindowThemeTests
{
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
                var confirmation = window.ConfirmExitAsync();
                Assert.That(window.ConfirmExitAsync(), Is.SameAs(confirmation));
                var cancel = window.FindControl<Button>("CancelExitButton")!;
                var content = window.FindControl<Grid>("PlayerContent")!;
                Assert.That(content.IsEnabled, Is.False);
                Assert.That(cancel.IsFocused, Is.True);
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var directory = Environment.GetEnvironmentVariable("GALNET_THEME_CAPTURE_DIR");
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var bitmap = window.CaptureRenderedFrame();
                    bitmap!.Save(Path.Combine(directory, "Sample-ExitConfirmation.png"));
                }
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.That(await confirmation, Is.False);
                Assert.That(content.IsEnabled, Is.True);
                confirmation = window.ConfirmExitAsync();
                window.FindControl<Button>("ConfirmExitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(await confirmation, Is.True);
            }
            finally { window.Close(); Application.Current.Styles.Remove(ursa); Application.Current.Styles.Remove(semi); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }
}
