using Avalonia.Controls;
using Avalonia.Headless;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.Themes;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class ScreenshotOverlayHostTests
{
    [Test]
    public async Task Automatic_destination_hides_path_edits_retries_errors_and_exports_full_capture()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var destination = new RecordingDestination();
            var registrations = new ServiceCollection().AddAvaloniaGameViewPages(theme: new ClassicTheme());
            registrations.AddSingleton<IAutomaticGameScreenshotDestination>(destination);
            await using var services = registrations.BuildServiceProvider();
            var host = new OverlayDialogHost { IsAnimationDisabled = true };
            var window = new Window { Content = host };
            window.Show();
            try
            {
                var captures = new List<bool>();
                var capture = services.GetRequiredService<IGameScreenshotService>().CaptureAsync(new("Mobile Game", window,
                    includeUi => { captures.Add(includeUi); return Task.FromResult<byte[]>([1, 2, 3]); })
                {
                    CapturePreviewPngAsync = _ => Task.FromException<byte[]>(new IOException("Preview is unavailable."))
                });
                var dialog = (ScreenshotDialog)host.Children.OfType<CustomDialogControl>().Single().Content!;
                var model = (ScreenshotDialogViewModel)dialog.DataContext!;
                Assert.That(model.CanChooseDestination, Is.False);
                var generatedName = model.FileName;
                model.DirectoryPath = "ignored";
                model.FileName = "ignored.png";
                model.IncludeUi = true;
                await model.SaveAsync();
                Assert.That(model.Error, Does.Contain("retry"));
                Assert.That(capture.IsCompleted, Is.False);
                await model.SaveAsync();
                await capture.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(captures, Is.EqualTo(new[] { true, true }));
                Assert.That(destination.GameTitle, Is.EqualTo("Mobile Game"));
                Assert.That(destination.FileName, Is.EqualTo(generatedName));
                Assert.That(destination.Png, Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(host.Children, Is.Empty);
            }
            finally { window.Close(); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task Screenshot_uses_the_requested_top_level_and_stays_modal_until_cancelled()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var theme = new ClassicTheme();
            await using var services = new ServiceCollection().AddAvaloniaGameViewPages(theme: theme).BuildServiceProvider();
            var firstHost = new OverlayDialogHost { IsAnimationDisabled = true };
            var targetHost = new OverlayDialogHost { IsAnimationDisabled = true };
            var first = new Window { Content = firstHost };
            var target = new Window { Content = targetHost };
            first.Show();
            target.Show();
            try
            {
                var screenshots = services.GetRequiredService<IGameScreenshotService>();
                var capture = screenshots.CaptureAsync(new("Test", target,
                    _ => Task.FromException<byte[]>(new IOException("Preview is unavailable."))));
                Assert.That(firstHost.Children, Is.Empty, "A screenshot must not use another player's overlay.");
                Assert.That(targetHost.Children.OfType<CustomDialogControl>().Count(), Is.EqualTo(1));
                Assert.That(targetHost.IsInModalStatus, Is.True);
                Assert.That(capture.IsCompleted, Is.False);
                var control = targetHost.Children.OfType<CustomDialogControl>().Single();
                var dialog = (ScreenshotDialog)control.Content!;
                Assert.That(dialog.DataContext, Is.TypeOf<ScreenshotDialogViewModel>());
                Assert.That(((ScreenshotDialogViewModel)dialog.DataContext!).CanChooseDestination, Is.True);
                ((ScreenshotDialogViewModel)dialog.DataContext!).Cancel();
                await capture.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(targetHost.IsInModalStatus, Is.False);
                Assert.That(targetHost.Children, Is.Empty);
            }
            finally
            {
                first.Close();
                target.Close();
            }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    private sealed class RecordingDestination : IAutomaticGameScreenshotDestination
    {
        private bool _fail = true;
        public string? GameTitle { get; private set; }
        public string? FileName { get; private set; }
        public byte[]? Png { get; private set; }

        public Task SaveAsync(string gameTitle, string fileName, ReadOnlyMemory<byte> png, CancellationToken cancellationToken = default)
        {
            if (_fail) { _fail = false; throw new IOException("retry"); }
            GameTitle = gameTitle;
            FileName = fileName;
            Png = png.ToArray();
            return Task.CompletedTask;
        }
    }
}
