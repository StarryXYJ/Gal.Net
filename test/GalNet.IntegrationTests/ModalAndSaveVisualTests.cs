using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.Themes;
using GalNet.Game.Controls;
using GalNet.Sample.CrossPlatform;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class ModalAndSaveVisualTests
{
    [TestCase("Mono")]
    [TestCase("Ocean")]
    [TestCase("Rose")]
    public Task SavePreviewPixelsRemainVisibleAboveTheActivationTarget(string palette) => SampleUiTests.RunAsync(() =>
    {
        using var pixels = new SKBitmap(480, 270);
        pixels.Erase(SKColors.CornflowerBlue);
        using var encoded = pixels.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(encoded.ToArray());
        using var preview = new global::Avalonia.Media.Imaging.Bitmap(stream);
        var activations = 0;
        var card = new SaveSlotCard { Width = 360, Preview = preview, IsEmpty = false, Description = "Known preview",
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => activations++) };
        var root = new Grid { Classes = { "galnet-player" }, Children = { card } };
        root.Styles.Add(SampleCustomization.CreateStyles(palette switch { "Ocean" => GamePalette.Ocean, "Rose" => GamePalette.Rose, _ => GamePalette.Mono }));
        var window = new Window { Width = 640, Height = 420, Content = root };
        window.Show();
        try
        {
            window.UpdateLayout();
            var image = card.GetVisualDescendants().OfType<Image>().Single();
            var point = image.TranslatePoint(new Point(image.Bounds.Width / 2, 30), window)!.Value;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!;
            using var png = new MemoryStream();
            frame.Save(png);
            using var captured = SKBitmap.Decode(png.ToArray());
            var color = captured.GetPixel((int)point.X, (int)point.Y);
            var target = card.GetVisualDescendants().OfType<Button>().Single();
            TestContext.WriteLine($"Preview source={image.Source is not null}, target background={target.Background}, pixel={color}");
            Capture(frame, $"save-preview-{palette}");
            Assert.That(color, Is.EqualTo(SKColors.CornflowerBlue), "A non-null image source is insufficient: the saved picture must actually be painted.");
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.That(activations, Is.EqualTo(1));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Test]
    public Task ModalKeepsHomeTextColorsAndStillBlocksBackgroundInput() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services => SampleCustomization.ConfigureServices(services));
        host.Services.GetRequiredService<IPlayerApplication>().CompleteStartup(null);
        var title = host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.ViewModels.TitlePageViewModel>();
        var navigation = host.Services.GetRequiredService<IGameNavigationService>();
        var window = new Window { Width = 1280, Height = 720, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            var about = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, title.OpenAboutCommand));
            var original = ((ISolidColorBrush)about.Foreground!).Color;
            var confirmation = host.ConfirmExitAsync();
            window.UpdateLayout();
            Assert.That(((ISolidColorBrush)about.Foreground!).Color, Is.EqualTo(original));
            Assert.That(about.IsEffectivelyEnabled, Is.True, "The modal overlay blocks pointer input without disabling the background controls.");
            var center = about.TranslatePoint(new Point(about.Bounds.Width / 2, about.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(title));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            var overlay = host.View.FindControl<Grid>("ExitConfirmation")!;
            Assert.That(((Visual)window.FocusManager!.GetFocusedElement()!).GetSelfAndVisualAncestors(), Does.Contain(overlay));
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var frame = window.CaptureRenderedFrame()!) Capture(frame, "home-modal-colors");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.That(await confirmation, Is.False);
            Assert.That(about.IsEffectivelyEnabled, Is.True);
            Assert.That(((ISolidColorBrush)about.Foreground!).Color, Is.EqualTo(original));
        }
        finally { window.Close(); }
    });

    private static void Capture(global::Avalonia.Media.Imaging.Bitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("GALNET_MODAL_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}
