using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using GalNet.Avalonia.Themes;
using GalNet.Sample.CrossPlatform;
using SkiaSharp;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class PlayerInteractionVisualTests
{
    [TestCase("Mono")]
    [TestCase("Ocean")]
    [TestCase("Rose")]
    public Task ButtonPressChangesOnlyItsFillAndNeverFlashesBlack(string paletteName) => SampleUiTests.RunAsync(() =>
    {
        var palette = paletteName switch { "Ocean" => GamePalette.Ocean, "Rose" => GamePalette.Rose, _ => GamePalette.Mono };
        var panel = new StackPanel { Classes = { "galnet-player" }, Spacing = 16, Margin = new Thickness(24) };
        panel.Styles.Add(SampleCustomization.CreateStyles(palette));
        var window = new Window { Width = 400, Height = 360, Content = panel };
        window.Background = (IBrush)panel.FindResource("GalNet.Brush.Background")!;
        foreach (var kind in new[] { "player-back", "player-menu", "player-link", "player-primary" })
            panel.Children.Add(new Button { Classes = { kind }, Content = kind, Width = 240 });
        window.Show();
        try
        {
            foreach (var button in panel.Children.OfType<Button>())
            {
                window.MouseMove(new Point(1, 1));
                window.UpdateLayout();
                var foreground = ((ISolidColorBrush)button.Foreground!).Color;
                var border = ((ISolidColorBrush)button.BorderBrush!).Color;
                var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                var labelColor = ((ISolidColorBrush)label.Foreground!).Color;
                var bounds = button.Bounds;
                var weight = label.FontWeight;
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                var hover = Pixel(window, button);
                window.MouseDown(point, MouseButton.Left);
                window.UpdateLayout();
                var pressed = Pixel(window, button);
                Assert.That(button.IsPressed, Is.True);
                Assert.That(button.RenderTransform?.Value.IsIdentity ?? true, Is.True, "A custom template must also suppress the base theme's press scale.");
                var directory = Environment.GetEnvironmentVariable("GALNET_MOBILE_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    using var screenshot = window.CaptureRenderedFrame()!;
                    screenshot.Save(Path.Combine(directory, $"button-{paletteName}-{button.Classes[0]}-pressed.png"));
                }
                Assert.That(pressed, Is.Not.EqualTo(hover));
                Assert.That(((ISolidColorBrush)button.Foreground!).Color, Is.EqualTo(foreground));
                Assert.That(((ISolidColorBrush)button.BorderBrush!).Color, Is.EqualTo(border));
                Assert.That(((ISolidColorBrush)label.Foreground!).Color, Is.EqualTo(labelColor));
                Assert.That(label.FontWeight, Is.EqualTo(weight));
                Assert.That(button.Bounds, Is.EqualTo(bounds));
                if (paletteName == "Mono")
                {
                    Assert.That(pressed.Red + pressed.Green + pressed.Blue, Is.LessThan(hover.Red + hover.Green + hover.Blue));
                    Assert.That(Math.Min(pressed.Red, Math.Min(pressed.Green, pressed.Blue)), Is.GreaterThan(50));
                }
                window.MouseMove(new Point(1, 1));
                window.MouseUp(new Point(1, 1), MouseButton.Left);
                button.IsEnabled = false;
                window.UpdateLayout();
                var disabled = Pixel(window, button);
                window.MouseMove(point);
                Assert.That(Pixel(window, button), Is.EqualTo(disabled), "Unavailable actions do not show hover feedback.");
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static SKColor Pixel(Window window, Control control)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        frame.Save(stream);
        using var bitmap = SKBitmap.Decode(stream.ToArray());
        var point = control.TranslatePoint(new Point(8, 8), window)!.Value;
        return bitmap.GetPixel((int)point.X, (int)point.Y);
    }
}
