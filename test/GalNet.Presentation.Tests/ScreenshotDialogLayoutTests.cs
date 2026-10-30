using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.Themes;
using GalNet.Avalonia.Themes.Layouts.Classic;
using GalNet.Avalonia.Themes.Layouts.Minimal;
using GalNet.Avalonia.Themes.Layouts.Paper;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class ScreenshotDialogLayoutTests
{
    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task Automatic_destination_remains_compact_after_portrait_and_landscape_resize(string layout)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(() =>
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(160, 90));
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            var bytes = stream.ToArray();
            using ScreenshotDialog dialog = layout switch
            {
                "Classic" => new ClassicScreenshotDialog(),
                "Minimal" => new MinimalScreenshotDialog(),
                _ => new PaperScreenshotDialog()
            };
            dialog.Styles.Add(new ClassicTheme().CreateStyles());
            dialog.DataContext = new ScreenshotDialogViewModel("", "automatic.png",
                _ => Task.FromResult(bytes), (_, _, _) => Task.FromResult<string?>(null))
            { CanChooseDestination = false };
            dialog.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
            dialog.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
            var window = new Window { Width = 853, Height = 384, Content = dialog };
            window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
            window.Show();
            try
            {
                foreach (var size in new[] { new Size(853, 384), new Size(384, 853), new Size(700, 320) })
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.That(dialog.FindControl<Grid>("DirectoryPanel")!.IsVisible, Is.False);
                    Assert.That(dialog.FindControl<StackPanel>("FileNamePanel")!.IsVisible, Is.False);
                    Assert.That(dialog.Bounds.Width, Is.LessThanOrEqualTo(Math.Min(480, size.Width - 32)));
                    Assert.That(dialog.Bounds.Height, Is.LessThanOrEqualTo(size.Height - 24));
                    foreach (var name in new[] { "CancelButton", "SaveButton" })
                    {
                        var button = dialog.FindControl<Button>(name)!;
                        var origin = button.TranslatePoint(default, window)!.Value;
                        Assert.That(origin.X, Is.GreaterThanOrEqualTo(0), name);
                        Assert.That(origin.Y, Is.GreaterThanOrEqualTo(0), name);
                        Assert.That(origin.X + button.Bounds.Width, Is.LessThanOrEqualTo(size.Width), name);
                        Assert.That(origin.Y + button.Bounds.Height, Is.LessThanOrEqualTo(size.Height), name);
                    }
                    Assert.That(dialog.GetVisualDescendants().OfType<Image>().Single().Source, Is.Not.Null);
                }
            }
            finally { window.Close(); }
            return Task.FromResult(true);
        }, TestContext.CurrentContext.CancellationToken);
    }
}
