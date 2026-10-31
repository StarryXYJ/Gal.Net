using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GalNet.Game.Controls.Windowing;
using GalNet.Avalonia.GameHost;
using DynamicLocalization.Core;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Services;
using GalNet.Runtime.Variables;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class DesktopWindowPlacementTests
{
    [Test]
    public Task PlayerSettingsRestorePlacementBeforeFullscreenWithoutRewritingPreferences() => SampleUiTests.RunAsync(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-placement-settings-" + Guid.NewGuid().ToString("N"));
        var window = new Window { Width = 1280, Height = 720 };
        _ = new DesktopWindowPlacement(window, new WindowPlacement(900, 600, true), _ => { });
        window.Show();
        try
        {
            using var localization = new AvaloniaPlayerLocalization(new CultureService());
            using var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            using var display = new AvaloniaWindowDisplayService();
            using var settings = new AvaloniaPlayerSettingsService(localization, new GalNet.Avalonia.GameView.Presentation.GamePresentationState(), display);
            settings.BindWindow(window);
            await settings.AttachAsync(variables, new GameSettings());
            var mode = settings.Categories.Single(category => category.Id == "graphics").Items.OfType<ChoiceSetting<PlayerDisplayMode>>().Single();
            Assert.That(await mode.TrySetValueAsync(PlayerDisplayMode.BorderlessFullscreen), Is.True);
            await settings.FlushAsync();
            var path = Path.Combine(directory, "player-variables.json");
            var before = File.ReadAllText(path);
            await settings.RestoreDisplayAsync();
            Assert.That(window.WindowState, Is.EqualTo(WindowState.FullScreen));
            Assert.That(display.WindowSize, Is.EqualTo(new PlayerWindowSize(900, 600)));
            await display.ApplyModeAsync(PlayerDisplayMode.Window, default);
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized));
            Assert.That(window.Width, Is.EqualTo(900));
            Assert.That(window.Height, Is.EqualTo(600));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        }
        finally
        {
            window.Close();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    });

    [TestCase(1600, 900, 1280, 720)]
    [TestCase(1600, 900, 640, 360)]
    [TestCase(1600, 900, 1920, 1080)] // e.g. a 3840x2160 screen at 200% scaling
    public void OversizeWindowFitsEvenWhenScreenIsSmallerThanMinimum(double width, double height, double availableWidth, double availableHeight)
    {
        var size = DesktopWindowPlacement.Fit(new(width, height), new(960, 640), new(availableWidth, availableHeight));
        Assert.That(size.Width, Is.InRange(1, availableWidth * .95));
        Assert.That(size.Height, Is.InRange(1, availableHeight * .95));
        Assert.That(size.Width / size.Height, Is.EqualTo(width / height).Within(.000001));
    }

    [Test]
    public void NormalSizeAndInvalidSavedDimensions()
    {
        Assert.That(DesktopWindowPlacement.Fit(new(1000, 700), new(800, 500), new(1920, 1080)), Is.EqualTo(new Size(1000, 700)));
        var size = DesktopWindowPlacement.Fit(new(double.NaN, -1), new(800, 500), new(1280, 720));
        Assert.That(double.IsFinite(size.Width) && double.IsFinite(size.Height), Is.True);
        Assert.That(size.Height, Is.InRange(1, 684));
    }

    [Test]
    public void FileRoundTripAndCorruptionFallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-window-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "window-placement.json");
        try
        {
            Assert.That(DesktopWindowPlacement.ReadFile(path), Is.Null);
            var saved = new WindowPlacement(1100, 700, true);
            DesktopWindowPlacement.WriteFile(path, saved);
            Assert.That(DesktopWindowPlacement.ReadFile(path), Is.EqualTo(saved));
            DesktopWindowPlacement.WriteFile(path, saved with { Maximized = false });
            Assert.That(DesktopWindowPlacement.ReadFile(path)!.Maximized, Is.False);
            File.WriteAllText(path, "invalid json");
            Assert.That(DesktopWindowPlacement.ReadFile(path), Is.Null);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public Task ConfirmedCloseSavesOnceAndFullscreenDoesNotReplaceNormalSize() => SampleUiTests.RunAsync(async () =>
    {
        var saves = new List<WindowPlacement>();
        var window = new Window { Width = 1000, Height = 700 };
        _ = new DesktopWindowPlacement(window, new WindowPlacement(1000, 700, false), saves.Add);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            using var display = new AvaloniaWindowDisplayService();
            display.Bind(window);
            window.Width = 900;
            window.Height = 600;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var normal = window.ClientSize;
            await display.ApplyModeAsync(PlayerDisplayMode.BorderlessFullscreen, default);
            EventHandler<WindowClosingEventArgs> cancel = (_, args) => args.Cancel = true;
            window.Closing += cancel;
            window.Close();
            Assert.That(saves, Is.Empty);
            window.Closing -= cancel;
            window.Close();
            Assert.That(saves, Has.Count.EqualTo(1));
            Assert.That(saves[0].Width, Is.EqualTo(normal.Width));
            Assert.That(saves[0].Height, Is.EqualTo(normal.Height));
            Assert.That(saves[0].Maximized, Is.False);
        }
        finally { window.Close(); }
    });

    [Test]
    public Task SavedGeometryWinsOverLegacySizeBeforeFullscreen() => SampleUiTests.RunAsync(async () =>
    {
        WindowPlacement? result = null;
        var window = new Window { Width = 1280, Height = 720 };
        _ = new DesktopWindowPlacement(window, new WindowPlacement(900, 600, true), saved => result = saved);
        window.Show();
        try
        {
            using var display = new AvaloniaWindowDisplayService();
            display.Bind(window);
            await display.ApplyWindowSizeAsync(new(1280, 720), default);
            DesktopWindowPlacement.RestoreDisplayPreferences(window, new Size(1280, 720));
            Assert.That(window.Width, Is.EqualTo(900));
            Assert.That(window.Height, Is.EqualTo(600));
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized));
            await display.ApplyModeAsync(PlayerDisplayMode.BorderlessFullscreen, default);
            await display.ApplyModeAsync(PlayerDisplayMode.Window, default);
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized));
            // Rebuilding the game session must keep the user's current geometry.
            window.WindowState = WindowState.Normal;
            window.Width = 950;
            window.Height = 650;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            DesktopWindowPlacement.RestoreDisplayPreferences(window, new Size(1280, 720));
            Assert.That(window.Width, Is.EqualTo(950));
            Assert.That(window.Height, Is.EqualTo(650));
            window.WindowState = WindowState.Maximized;
            window.WindowState = WindowState.Minimized;
        }
        finally { window.Close(); }
        Assert.That(result!.Maximized, Is.True);
        Assert.That(result.Width, Is.EqualTo(950));
        Assert.That(result.Height, Is.EqualTo(650));
    });
}
