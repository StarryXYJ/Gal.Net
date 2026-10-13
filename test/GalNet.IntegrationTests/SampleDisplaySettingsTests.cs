using GalNet.Runtime.Variables;
using DynamicLocalization.Core;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Settings;
using GalNet.Sample.Avalonia.Services;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class SampleDisplaySettingsTests
{
    [Test]
    public async Task FullscreenDisablesWindowSizeAndPreferencesRestoreWithoutWritingAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-display-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var localization = new SampleLocalizationService(new CultureService());
            using var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            var display = new Display();
            using (var settings = new SamplePlayerSettingsService(localization, new GamePageViewModel(null!), display))
            {
                await settings.AttachAsync(variables, new GameSettings());
                var category = settings.Categories.Single(category => category.Id == "graphics");
                var mode = category.Items.OfType<ChoiceSetting<PlayerDisplayMode>>().Single();
                var size = category.Items.OfType<ChoiceSetting<PlayerWindowSize>>().Single();
                Assert.That(mode.Choices.Select(choice => choice.Value), Does.Not.Contain(PlayerDisplayMode.ExclusiveFullscreen));
                Assert.That(await size.TrySetValueAsync(new(1920, 1080)), Is.True);
                Assert.That(await mode.TrySetValueAsync(PlayerDisplayMode.BorderlessFullscreen), Is.True);
                Assert.That(size.IsEnabled, Is.False);
                Assert.That(size.Availability.ReasonKey, Is.EqualTo("Settings.WindowSizeFullscreen"));
                Assert.That(await size.TrySetValueAsync(new(1280, 720)), Is.False);
                Assert.That(display.WindowSize, Is.EqualTo(new PlayerWindowSize(1920, 1080)));
                await settings.FlushAsync();
            }
            var restoredDisplay = new Display();
            using var restored = new SamplePlayerSettingsService(localization, new GamePageViewModel(null!), restoredDisplay);
            await restored.AttachAsync(variables, new GameSettings());
            var before = File.ReadAllText(Path.Combine(directory, "player-variables.json"));
            await restored.RestoreDisplayAsync();
            Assert.That(restoredDisplay.Mode, Is.EqualTo(PlayerDisplayMode.BorderlessFullscreen));
            Assert.That(restoredDisplay.WindowSize, Is.EqualTo(new PlayerWindowSize(1920, 1080)));
            Assert.That(File.ReadAllText(Path.Combine(directory, "player-variables.json")), Is.EqualTo(before));
            var restoredCategory = restored.Categories.Single(category => category.Id == "graphics");
            restoredDisplay.SetAvailable(false);
            Assert.That(restoredCategory.Items.All(item => !item.IsVisible), Is.True);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task FailedModeChangeReportsErrorAndDoesNotPersistAnUnappliedValue()
    {
        using var localization = new SampleLocalizationService(new CultureService());
        var display = new Display { FailMode = true };
        using var settings = new SamplePlayerSettingsService(localization, new GamePageViewModel(null!), display);
        var mode = settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<PlayerDisplayMode>>().Single();
        Assert.That(await mode.TrySetValueAsync(PlayerDisplayMode.BorderlessFullscreen), Is.False);
        Assert.That(mode.Value, Is.EqualTo(PlayerDisplayMode.Window));
        Assert.That(mode.ErrorKey, Is.EqualTo("Settings.ApplyFailed"));
    }

    private sealed class Display : IPlayerDisplayService
    {
        public bool IsAvailable { get; private set; } = true;
        public bool FailMode { get; init; }
        public IReadOnlyList<PlayerDisplayMode> Modes => [PlayerDisplayMode.Window, PlayerDisplayMode.BorderlessFullscreen];
        public IReadOnlyList<PlayerWindowSize> WindowSizes => [new(1280, 720), new(1920, 1080)];
        public PlayerDisplayMode Mode { get; private set; }
        public PlayerWindowSize WindowSize { get; private set; } = new(1280, 720);
        public event Action? Changed;
        public void SetAvailable(bool available) { IsAvailable = available; Changed?.Invoke(); }
        public Task ApplyModeAsync(PlayerDisplayMode mode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailMode) throw new InvalidOperationException("backend refused");
            Mode = mode;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
        public Task ApplyWindowSizeAsync(PlayerWindowSize size, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(Mode, Is.EqualTo(PlayerDisplayMode.Window));
            WindowSize = size;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }
}
