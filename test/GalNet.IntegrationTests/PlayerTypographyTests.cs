using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Game.Controls;
using GalNet.GameHost;
using GalNet.Runtime.Variables;
using GalNet.Sample.CrossPlatform;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class PlayerTypographyTests
{
    [Test]
    public Task ChangingBaseSizeDuringTypingKeepsTheSameRunAndCompletion() => SampleUiTests.RunAsync(async () =>
    {
        var text = new TypewriterTextBlock { FontSize = 20, CharactersPerSecond = 2,
            SourceText = "<size=+4>正在逐字显示的内容</size>" };
        var completion = text.Completion;
        var run = text.Inlines!.OfType<Run>().Single();
        var visible = run.Text;
        Assert.That(text.IsRunning, Is.True);
        text.FontSize = 30;
        Assert.Multiple(() =>
        {
            Assert.That(text.Completion, Is.SameAs(completion));
            Assert.That(text.Inlines!.OfType<Run>().Single(), Is.SameAs(run));
            Assert.That(run.Text, Is.EqualTo(visible));
            Assert.That(run.FontSize, Is.EqualTo(34));
            Assert.That(text.IsRunning, Is.True);
        });
        text.RevealAll();
        await completion;
        Assert.That(run.Text, Is.EqualTo("正在逐字显示的内容"));
    });

    [TestCase(false, FontSizePreset.Smaller)]
    [TestCase(true, FontSizePreset.Larger)]
    public Task PlatformDefaultIsUsedUntilAValidPlayerChoiceIsRestored(bool mobile, FontSizePreset expected)
        => SampleUiTests.RunAsync(async () =>
    {
        var directory = CreateOutputDirectory();
        try
        {
            using var localization = SampleCustomization.CreateLocalizationService();
            using var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            var defaultSize = PlayerTypography.DefaultForPlatform(mobile);
            Assert.That(defaultSize, Is.EqualTo(expected));
            using (var settings = new AvaloniaPlayerSettingsService(localization, new GamePresentationState(), defaultFontSize: defaultSize))
            {
                Assert.That(settings.RuntimeSettings.FontSize, Is.EqualTo(expected));
                await settings.AttachAsync(variables, new GameSettings());
                var choice = FontSizeSetting(settings);
                Assert.That(choice.Value, Is.EqualTo(expected));
                Assert.That(choice.Choices.Count, Is.EqualTo(4));
                localization.ApplyCulture("zh-CN");
                Assert.That(choice.Choices.Select(option => option.Label!.ToString()), Is.EqualTo(new[] { "小", "较小", "较大", "大" }));
                Assert.That(await choice.TrySetValueAsync(FontSizePreset.Large), Is.True);
                await settings.FlushAsync();
            }
            var preferenceFile = Path.Combine(directory, "player-variables.json");
            var before = File.ReadAllText(preferenceFile);
            using var restoredVariables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            using var restored = new AvaloniaPlayerSettingsService(localization, new GamePresentationState(), defaultFontSize: defaultSize);
            await restored.AttachAsync(restoredVariables, new GameSettings());
            Assert.That(restored.RuntimeSettings.FontSize, Is.EqualTo(FontSizePreset.Large));
            Assert.That(File.ReadAllText(preferenceFile), Is.EqualTo(before), "Restoring a choice must not rewrite preferences.");
            new PlayerPreferences(restoredVariables).Set("player.galnet.settings.fontSize", "removed-preset");
            await restored.AttachAsync(restoredVariables, new GameSettings());
            Assert.That(restored.RuntimeSettings.FontSize, Is.EqualTo(expected));
            Assert.That(await FontSizeSetting(restored).TrySetValueAsync((FontSizePreset)99), Is.False);
        }
        finally { Directory.Delete(directory, true); }
    });

    [Test]
    public Task LivePreferenceUpdatesActualTitleDialogueNvlAndHistoryWithoutReplayingText()
        => SampleUiTests.RunAsync(async () =>
    {
        var directory = CreateOutputDirectory();
        try
        {
            await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services => SampleCustomization.ConfigureServices(services));
            using var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            var settings = (AvaloniaPlayerSettingsService)host.Settings;
            await settings.AttachAsync(variables, new GameSettings());
            host.Services.GetRequiredService<IPlayerApplication>().CompleteStartup(null);
            var window = new Window { Width = 1280, Height = 720, Content = host.View };
            window.Show();
            try
            {
                window.UpdateLayout();
                var scale = PlayerLayout.UiScale(new global::Avalonia.Size(1280, 720));
                var title = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.FontSize == 64 * scale);
                Assert.That(await FontSizeSetting(settings).TrySetValueAsync(FontSizePreset.Larger), Is.True);
                window.UpdateLayout();
                var larger = PlayerTypography.Presets[FontSizePreset.Larger];
                var large = PlayerTypography.Presets[FontSizePreset.Large];
                Assert.That(title.FontSize, Is.EqualTo(larger.Title * scale));
                var navigation = host.Services.GetRequiredService<IGameNavigationService>();
                navigation.ResetTo<GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel>(NavigationTransition.None);
                var page = host.Services.GetRequiredService<GamePage>();
                var state = host.Services.GetRequiredService<GamePresentationState>();
                state.IsDialogueVisible = true;
                state.IsNvlMode = true;
                page.Dialogue.Speaker = "角色名";
                page.Dialogue.CharactersPerSecond = 0;
                page.Dialogue.Text = "正文<size=+4>强调<size=125%>嵌套</size></size>";
                window.UpdateLayout();
                await page.Dialogue.StartAsync();
                await page.Nvl.AppendAsync("NVL<size=-2>轻声</size>", 0);
                window.UpdateLayout();
                var dialogue = page.Dialogue.GetVisualDescendants().OfType<TypewriterTextBlock>().Single();
                var nvl = page.Nvl.GetVisualDescendants().OfType<TypewriterTextBlock>().Single();
                var speaker = page.Dialogue.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "角色名");
                Assert.That((dialogue.FontSize, nvl.FontSize, speaker.FontSize), Is.EqualTo((larger.Dialogue, larger.Dialogue, larger.Emphasis)));
                var before = dialogue.DesiredSize.Height;
                var completion = dialogue.Completion;
                Assert.That(await FontSizeSetting(settings).TrySetValueAsync(FontSizePreset.Large), Is.True);
                window.UpdateLayout();
                Assert.That((dialogue.FontSize, nvl.FontSize, speaker.FontSize), Is.EqualTo((large.Dialogue, large.Dialogue, large.Emphasis)));
                Assert.That(dialogue.Inlines!.OfType<Run>().Select(run => run.FontSize), Is.EqualTo(new[] { large.Dialogue, large.Dialogue + 4, (large.Dialogue + 4) * 1.25 }));
                Assert.That(dialogue.DesiredSize.Height, Is.GreaterThan(before));
                Assert.That(dialogue.Completion, Is.SameAs(completion));
                state.HistoryLines = [new("g", "e", "角色名", "历史<size=1.25em>强调</size>", false)];
                state.IsHistoryOpen = true;
                window.UpdateLayout();
                var history = page.History.GetVisualDescendants().OfType<TypewriterTextBlock>().Single();
                Assert.That(history.FontSize, Is.EqualTo(large.Body * 21 / 18 * scale));
                Assert.That(history.Inlines!.OfType<Run>().Last().FontSize, Is.EqualTo(large.Body * 21 / 18 * scale * 1.25));
                using (var screenshot = new GalNet.Sample.CrossPlatform.Views.ScreenshotDialog(settings))
                    Assert.That(screenshot.FindResource("GalNet.Theme.DialogueFontSize"), Is.EqualTo(large.Dialogue));
                Assert.That(await FontSizeSetting(settings).TrySetValueAsync(FontSizePreset.Smaller), Is.True);
                window.UpdateLayout();
                Assert.That((dialogue.FontSize, nvl.FontSize, speaker.FontSize, history.FontSize), Is.EqualTo((22d, 22d, 26d, 21d * scale)));
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(directory, true); }
    });

    [Test]
    public void ProjectCanConfigureSemanticRolesIndependentlyAndInvalidSizesDoNotPartiallyApply()
    {
        var original = PlayerTypography.Presets[FontSizePreset.Larger];
        try
        {
            PlayerTypography.Presets[FontSizePreset.Larger] = original with { Dialogue = 31, Heading = 45 };
            var resources = new ResourceDictionary();
            PlayerTypography.Apply(resources, FontSizePreset.Larger);
            Assert.That(resources["GalNet.Theme.DialogueFontSize"], Is.EqualTo(31));
            Assert.That(resources["GalNet.Theme.HeadingSize"], Is.EqualTo(45));
            Assert.That(resources["GalNet.Theme.SpeakerFontSize"], Is.EqualTo(original.Emphasis));
            PlayerTypography.Presets[FontSizePreset.Larger] = original with { Dialogue = double.NaN };
            Assert.Throws<ArgumentOutOfRangeException>(() => PlayerTypography.Apply(resources, FontSizePreset.Larger));
            Assert.That(resources["GalNet.Theme.DialogueFontSize"], Is.EqualTo(31));
        }
        finally { PlayerTypography.Presets[FontSizePreset.Larger] = original; }
    }

    private static ChoiceSetting<FontSizePreset> FontSizeSetting(AvaloniaPlayerSettingsService settings) =>
        settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<FontSizePreset>>().Single();

    private static string CreateOutputDirectory()
    {
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../output"));
        var directory = Path.GetFullPath(Path.Combine(output, "player-typography-" + Guid.NewGuid().ToString("N")));
        Assert.That(directory.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
