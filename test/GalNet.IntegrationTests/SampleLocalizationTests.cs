using Avalonia.Controls;
using Avalonia.Data;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Avalonia.GameView.MarkupExtensions;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Settings;
using GalNet.Sample.Avalonia;
using GalNet.Sample.Avalonia.Services;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

[TestFixture]
public sealed class SampleLocalizationTests
{
    [Test]
    public void DynamicSettingKeysResolveAndRefreshWhenLanguageOrReasonChanges()
    {
        using var localization = CreateLocalization();
        var previous = PlayerTextExtension.Resolve;
        try
        {
            PlayerTextExtension.Resolve = localization.GetString;
            var text = new PlayerTextBlock { Key = "Settings.SaveFailed" };
            Assert.That(text.Text, Does.Contain("could not save"));
            localization.ApplyCulture("zh-CN");
            Assert.That(text.Text, Does.Contain("保存失败"));
            text.Key = "Settings.OutOfRange";
            Assert.That(text.Text, Does.Contain("允许范围"));
            text.Key = null;
            Assert.That(text.IsVisible, Is.False);
            Assert.That(text.Text, Is.Null);
        }
        finally { PlayerTextExtension.Resolve = previous; }
    }

    [TestCase(-1)]
    [TestCase(5001)]
    [TestCase(.05f)]
    public async Task InvalidSavedPlaybackPreferencesUseDefaultsWithoutChangingUnrelatedValues(object savedInterval)
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-invalid-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var localization = CreateLocalization();
            using var variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            var badInterval = new GalNet.Core.Variable.Variable { Name = "player.galnet.settings.quickInterval" };
            badInterval.SetValue(savedInterval);
            variables.NotifyVariableChanged(GalNet.Core.Variable.VariableScope.Player, badInterval.Name, badInterval);
            var badBool = new GalNet.Core.Variable.Variable { Name = "player.galnet.settings.quickReadOnly" };
            badBool.SetValue("false");
            variables.NotifyVariableChanged(GalNet.Core.Variable.VariableScope.Player, badBool.Name, badBool);
            using var settings = new SamplePlayerSettingsService(localization, new GamePageViewModel(null!));
            var runtime = new GameSettings { QuickReadOnly = false, QuickAdvanceIntervalMilliseconds = 3000 };
            await settings.AttachAsync(variables, runtime);
            Assert.That(runtime.QuickReadOnly, Is.True);
            Assert.That(runtime.QuickAdvanceIntervalMilliseconds, Is.EqualTo(50));
            Assert.That(variables.GetSnapshot(GalNet.Core.Variable.VariableScope.Player)[badBool.Name].AsString(), Is.EqualTo("false"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void ExistingBindingUpdatesWhenCultureServiceIsChangedDirectly()
    {
        using var localization = CreateLocalization();
        var text = new TextBlock();
        using var binding = text.Bind(TextBlock.TextProperty, new ReflectionBinding("Value")
        {
            Source = localization.GetString("Settings.Title"),
            Mode = BindingMode.OneWay
        });
        Assert.That(text.Text, Is.EqualTo("Settings"));
        localization.CultureService.SetCulture("zh-CN");
        Assert.That(text.Text, Is.EqualTo("设置"));
        localization.CultureService.SetCulture("en-US");
        Assert.That(text.Text, Is.EqualTo("Settings"));
    }

    [Test]
    public async Task SettingsPersistAndRestoreWithoutLosingOtherPlayerVariables()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var localization = CreateLocalization();
            using (var variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(directory)))
            using (var settings = new SamplePlayerSettingsService(localization, new GamePageViewModel(null!)))
            {
                var other = new GalNet.Core.Variable.Variable { Name = "player.other" };
                other.SetValue("preserved");
                variables.NotifyVariableChanged(GalNet.Core.Variable.VariableScope.Player, other.Name, other);
                var runtime = new GameSettings();
                await settings.AttachAsync(variables, runtime);
                var language = settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<string>>().Single();
                await language.TrySetValueAsync("zh-CN");
                var items = settings.Categories.SelectMany(category => category.Items).ToDictionary(item => item.Id);
                await ((DoubleSetting)items["textSpeed"]).TrySetValueAsync(65);
                await ((IntSetting)items["quickInterval"]).TrySetValueAsync(75);
                await ((IntSetting)items["autoInterval"]).TrySetValueAsync(3250);
                await ((BoolSetting)items["quickReadOnly"]).TrySetValueAsync(false);
                await ((DoubleSetting)items["masterVolume"]).TrySetValueAsync(.37);
                await ((BoolSetting)items["voiceMuted"]).TrySetValueAsync(true);
                Assert.That(runtime.QuickReadOnly, Is.False);
                Assert.That(runtime.VoiceMuted, Is.True);
                await settings.FlushAsync();
            }
            using var restoredLocalization = CreateLocalization();
            using var restoredVariables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            using var restoredSettings = new SamplePlayerSettingsService(restoredLocalization, new GamePageViewModel(null!));
            var restoredRuntime = new GameSettings();
            await restoredSettings.AttachAsync(restoredVariables, restoredRuntime);
            Assert.Multiple(() =>
            {
                Assert.That(restoredLocalization.CultureService.CurrentCulture.Name, Is.EqualTo("zh-CN"));
                Assert.That(restoredSettings.Categories.SelectMany(category => category.Items).OfType<DoubleSetting>().Single(item => item.Id == "textSpeed").Value, Is.EqualTo(65));
                Assert.That(restoredRuntime.QuickReadOnly, Is.False);
                Assert.That(restoredRuntime.QuickAdvanceIntervalMilliseconds, Is.EqualTo(75));
                Assert.That(restoredRuntime.AutoAdvanceIntervalMilliseconds, Is.EqualTo(3250));
                Assert.That(restoredVariables.GetSnapshot(GalNet.Core.Variable.VariableScope.Player)["player.galnet.settings.quickInterval"].Type, Is.EqualTo(GalNet.Core.Variable.VariableType.Int));
                Assert.That(restoredRuntime.MasterVolume, Is.EqualTo(.37).Within(.000001));
                Assert.That(restoredRuntime.VoiceMuted, Is.True);
                Assert.That(restoredVariables.GetSnapshot(GalNet.Core.Variable.VariableScope.Player)["player.other"].AsString(), Is.EqualTo("preserved"));
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static SampleLocalizationService CreateLocalization()
    {
        var provider = new JsonLocalizationProvider();
        provider.Initialize(new JsonLocalizationProviderOptions
        {
            BasePath = "Localization",
            UseEmbeddedResources = true,
            Assembly = typeof(App).Assembly
        });
        var culture = new CultureService();
        culture.RegisterProvider(provider);
        culture.SetCulture("en-US");
        return new SampleLocalizationService(culture, [provider]);
    }

    [Test]
    public async Task SettingsSnapshotLanguagesAfterContentLoadWithoutWatchingProviders()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-language-options-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var localization = CreateLocalization();
            using var settings = new SamplePlayerSettingsService(localization, new GamePageViewModel(null!));
            localization.AttachGame(new GalNet.Runtime.Content.GameContent
            {
                Graph = new(),
                Localization = new() { ExternalSources = [new("ja-JP", "external", """{"line":"日本語"}""")] }
            });
            using var variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            await settings.AttachAsync(variables, new GameSettings());
            var language = settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<string>>().Single();
            Assert.That(language.Choices.Select(option => option.Value), Does.Contain("ja-JP"));
            localization.AttachGame(new GalNet.Runtime.Content.GameContent { Graph = new() });
            Assert.That(language.Choices.Select(option => option.Value), Does.Contain("ja-JP"), "No runtime provider-change refresh.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void ReplacingGameSourcesPreservesEmbeddedUiProviderAndBinding()
    {
        using var localization = CreateLocalization();
        using var title = new LocalizedString(localization.CultureService, "App.Title");
        localization.AttachGame(new GalNet.Runtime.Content.GameContent { Graph = new() });
        localization.AttachGame(new GalNet.Runtime.Content.GameContent { Graph = new() });
        Assert.That(title.Value, Is.EqualTo("GalNet Avalonia Sample Player"));
        localization.CultureService.SetCulture("zh-CN");
        Assert.That(localization["Debug.Menu"], Is.EqualTo("调试"));
    }

    [Test]
    public void ExternalAndCustomSourcesOverrideUiAndGameWithoutLosingOtherKeys()
    {
        using var localization = CreateLocalization();
        using var title = new LocalizedString(localization.CultureService, "App.Title");
        var content = new GalNet.Runtime.Content.GameContent
        {
            Graph = new(),
            Localization = new()
            {
                Sources = [new("en-US", "en-US/game.json", """{"line":"builtin","unique":"kept"}""")],
                ExternalSources =
                [
                    new("en-US", "External/en-US/main.json", """{"App.Title":"Translated UI","line":"external"}"""),
                    new("ja-JP", "External/ja-JP/main.json", """{"line":"日本語"}""")
                ]
            }
        };
        localization.AttachGame(content);
        using var settings = new SamplePlayerSettingsService(localization, new GamePageViewModel(null!));
        Assert.That(title.Value, Is.EqualTo("Translated UI"));
        Assert.That(localization.Resolve("line"), Is.EqualTo("external"));
        Assert.That(localization.Resolve("unique"), Is.EqualTo("kept"));
        Assert.That(localization["Settings.Title"], Is.EqualTo("Settings"));
        Assert.That(settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<string>>().Single().Choices.Select(option => option.Value), Does.Contain("ja-JP"));
        localization.ApplyCulture("zh-CN");
        Assert.That(localization["Settings.Title"], Is.EqualTo("设置"), "Exact UI language precedes another language's overlay.");
        localization.ApplyCulture("en-US");
        localization.AttachGame(content, [new("en-US", "custom", """{"line":"custom"}""")]);
        Assert.That(localization.Resolve("line"), Is.EqualTo("custom"));
        localization.AttachGame(new GalNet.Runtime.Content.GameContent { Graph = new() });
        Assert.That(title.Value, Is.EqualTo("GalNet Avalonia Sample Player"));
    }

    [Test]
    public void EmbeddedResourcesResolveAndLaterJsonOverridesTheSameKey()
    {
        var provider = new JsonLocalizationProvider();
        provider.Initialize(new JsonLocalizationProviderOptions
        {
            BasePath = "Localization",
            UseEmbeddedResources = true,
            Assembly = typeof(App).Assembly
        });

        var cultureService = new CultureService();
        cultureService.RegisterProvider(provider);

        cultureService.SetCulture("en-US");
        Assert.That(cultureService["App.Title"], Is.EqualTo("GalNet Avalonia Sample Player"));

        cultureService.SetCulture("zh-CN");
        Assert.That(cultureService["Debug.Menu"], Is.EqualTo("调试"));

        provider.LoadJsonString("{\"App.Title\":\"External title\"}", "en-US");
        cultureService.SetCulture("en-US");
        Assert.That(cultureService["App.Title"], Is.EqualTo("External title"));
    }
}
