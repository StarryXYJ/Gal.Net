using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes;
using GalNet.Runtime.Variables;
using GalNet.Sample.Avalonia;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleReadTextColorTests
{
    [Test]
    public async Task CheckboxRevealsRealColorPickerAndPreferencesRestoreWithoutRewriting()
    {
        await SampleUiTests.RunAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "galnet-read-color-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var localization = SampleCustomization.CreateLocalizationService();
                var culture = localization.CultureService;
                using var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
                var game = new GamePageViewModel(null!);
                using (var settings = new AvaloniaPlayerSettingsService(localization, game))
                {
                    await settings.AttachAsync(variables, new GameSettings());
                    var category = settings.Categories.Single(item => item.Id == "game");
                    var colorSetting = category.Items.OfType<CustomSetting<string>>().Single(item => item.Id == "readTextColor");
                    Assert.That(category.DisplayItems.Any(item => item.Id == "enableReadTextColor"), Is.False);
                    using var editor = colorSetting.CreateEditor();
                    var root = new Border { Child = editor.Control, Classes = { "galnet-player" } };
                    root.Styles.Add(new ClassicTheme().CreateStyles());
                    var window = new Window { Width = 800, Height = 650, Content = root };
                    window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
                    window.Show();
                    try
                    {
                        window.UpdateLayout();
                        var checkbox = editor.Control.GetVisualDescendants().OfType<CheckBox>().Single();
                        var button = editor.Control.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "ReadTextColorPicker");
                        Assert.That(checkbox.IsChecked, Is.False);
                        Assert.That(button.IsVisible, Is.False);
                        Assert.That(game.ReadTextBrush, Is.Null);
                        checkbox.IsChecked = true;
                        await settings.FlushAsync();
                        Assert.That(button.IsVisible, Is.True);
                        Assert.That(((ISolidColorBrush)game.ReadTextBrush!).Color, Is.EqualTo(Color.Parse("#A8B8C8")));
                        button.Flyout!.ShowAt(button);
                        window.UpdateLayout();
                        var flyout = (Flyout)button.Flyout;
                        var picker = ((Control)flyout.Content!).GetVisualDescendants().OfType<ColorView>().Single();
                        Assert.That(picker.Template, Is.Not.Null, "The popup must load the ColorView theme.");
                        Assert.That(picker.GetVisualDescendants().Count(), Is.GreaterThan(5));
                        Assert.That(picker.Color, Is.EqualTo(Color.Parse("#A8B8C8")));
                        var actions = ((StackPanel)flyout.Content!).Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
                        Assert.That(actions.Select(control => control.Content), Is.EqualTo(new[] { "Cancel", "Confirm" }));
                        culture.SetCulture("zh-CN");
                        Assert.That(actions.Select(control => control.Content), Is.EqualTo(new[] { "取消", "确认" }));
                        culture.SetCulture("en-US");
                        Assert.That(actions.Select(control => control.Content), Is.EqualTo(new[] { "Cancel", "Confirm" }));
                        flyout.Hide();
                        Assert.That(await colorSetting.TrySetValueAsync("#123456"), Is.True);
                        Assert.That(((ISolidColorBrush)game.ReadTextBrush!).Color, Is.EqualTo(Color.Parse("#123456")));
                        Assert.That(await colorSetting.TrySetValueAsync("invalid"), Is.False);
                        await settings.FlushAsync();
                    }
                    finally { window.Close(); }
                }
                var stored = await File.ReadAllTextAsync(Path.Combine(directory, "player-variables.json"));
                var restoredGame = new GamePageViewModel(null!);
                using var restored = new AvaloniaPlayerSettingsService(localization, restoredGame);
                await restored.AttachAsync(variables, new GameSettings());
                Assert.That(restored.RuntimeSettings.EnableReadTextColor, Is.True);
                Assert.That(restored.RuntimeSettings.ReadTextColor, Is.EqualTo("#123456"));
                Assert.That(((ISolidColorBrush)restoredGame.ReadTextBrush!).Color, Is.EqualTo(Color.Parse("#123456")));
                Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "player-variables.json")), Is.EqualTo(stored));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
}
