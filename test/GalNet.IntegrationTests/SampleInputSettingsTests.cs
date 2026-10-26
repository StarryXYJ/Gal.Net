using DynamicLocalization.Core;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Input;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Runtime.Variables;
using GalNet.Sample.Avalonia.Services;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleInputSettingsTests
{
    [Test]
    public async Task BindingsUseExistingSettingsQueueAndRestoreWithoutRewriting()
    {
        await SampleUiTests.RunAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "galnet-input-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var localization = new AvaloniaPlayerLocalization(new CultureService());
                using (var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory)))
                using (var settings = new AvaloniaPlayerSettingsService(localization, new GamePageViewModel(null!)))
                {
                    await settings.AttachAsync(variables, new GameSettings());
                    Assert.That(settings.Categories.Select(category => category.Id), Is.EqualTo(new[] { "game", "operation", "graphics", "audio" }));
                    Assert.That(await settings.Input.SetBindingAsync("toggle.auto", PlayerInputDevice.KeyboardMouse, 0, "Control+MouseX1"), Is.True);
                    Assert.That(await settings.Input.SetLayoutAsync(true), Is.True);
                    await settings.FlushAsync();
                }
                var before = await File.ReadAllTextAsync(Path.Combine(directory, "player-variables.json"));
                using (var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory)))
                using (var restored = new AvaloniaPlayerSettingsService(localization, new GamePageViewModel(null!)))
                {
                    await restored.AttachAsync(variables, new GameSettings());
                    Assert.That(restored.Input.Resolve(PlayerInputDevice.KeyboardMouse, "Control+MouseX1"), Is.EqualTo("toggle.auto"));
                    Assert.That(restored.Input.EastConfirms, Is.True);
                    Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "player-variables.json")), Is.EqualTo(before));
                    await variables.ResetPlayerVariablesAsync();
                    await restored.AttachAsync(variables, new GameSettings());
                    Assert.That(restored.Input.Resolve(PlayerInputDevice.KeyboardMouse, "A"), Is.EqualTo("toggle.auto"));
                    Assert.That(restored.Input.EastConfirms, Is.False);
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
}
