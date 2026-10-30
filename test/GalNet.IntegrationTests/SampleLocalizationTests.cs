using Avalonia.Controls;
using Avalonia.Data;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.MarkupExtensions;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Runtime.Variables;
using GalNet.Sample.Avalonia;
using GalNet.Sample.Avalonia.Services;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

[TestFixture]
[NonParallelizable]
public sealed class SampleLocalizationTests
{
    [Test]
    public void SaveHeadingsAndScreenshotOptionFollowLanguageAndModeChanges()
    {
        using var localization = CreateLocalization();
        var previous = PlayerTextExtension.Resolve;
        try
        {
            PlayerTextExtension.Resolve = localization.GetString;
            var heading = new PlayerTextBlock { Key = "SaveSlots.SaveTitle", Fallback = "Save game" };
            Assert.That(heading.Text, Is.EqualTo("Save game"));
            localization.ApplyCulture("zh-CN");
            Assert.That(heading.Text, Is.EqualTo("保存进度"));
            heading.Key = "SaveSlots.LoadTitle";
            Assert.That(heading.Text, Is.EqualTo("读取存档"));
            Assert.That(localization.GetString("Screenshot.IncludeUi").Value, Is.EqualTo("包含对话框和操作栏"));
            localization.ApplyCulture("en-US");
            Assert.That(heading.Text, Is.EqualTo("Load game"));
            Assert.That(localization.GetString("Screenshot.IncludeUi").Value, Is.EqualTo("Include dialogue and controls"));
        }
        finally { PlayerTextExtension.Resolve = previous; }
    }

    [Test]
    public void SharedLocalizeBindingRefreshesAndPreservesFormatting()
    {
        using var localization = CreateLocalization();
        var previous = PlayerTextExtension.Resolve;
        try
        {
            PlayerTextExtension.Resolve = localization.GetString;
            var extension = new LocalizeExtension("Settings.Title") { StringFormat = "[{0}]" };
            var text = new TextBlock();
            using var binding = text.Bind(TextBlock.TextProperty, (ReflectionBinding)extension.ProvideValue(null!));
            Assert.That(text.Text, Is.EqualTo("[Settings]"));
            localization.CultureService.SetCulture("zh-CN");
            Assert.That(text.Text, Is.EqualTo("[设置]"));
            localization.CultureService.SetCulture("en-US");
            Assert.That(text.Text, Is.EqualTo("[Settings]"));

            PlayerTextExtension.Resolve = null;
            Assert.That(extension.ProvideValue(null!), Is.EqualTo("Settings.Title"));
            Assert.That(new LocalizeExtension().ProvideValue(null!), Is.EqualTo(string.Empty));
            Assert.That(new PlayerTextExtension("Settings.Title", "Settings fallback").ProvideValue(null!),
                Is.EqualTo("Settings fallback"));
        }
        finally { PlayerTextExtension.Resolve = previous; }
    }

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
            using var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            var badInterval = new GalNet.Core.Variable.Variable { Name = "player.galnet.settings.quickInterval" };
            badInterval.SetValue(savedInterval);
            variables.NotifyVariableChanged(GalNet.Core.Variable.VariableScope.Player, badInterval.Name, badInterval);
            var badBool = new GalNet.Core.Variable.Variable { Name = "player.galnet.settings.quickReadOnly" };
            badBool.SetValue("false");
            variables.NotifyVariableChanged(GalNet.Core.Variable.VariableScope.Player, badBool.Name, badBool);
            using var settings = new AvaloniaPlayerSettingsService(localization, new GamePageViewModel(null!));
            var runtime = new GameSettings { QuickReadOnly = false, QuickAdvanceIntervalMilliseconds = 3000 };
            await settings.AttachAsync(variables, runtime, GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create());
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
        => await SampleUiTests.RunAsync(SettingsPersistAndRestoreWithoutLosingOtherPlayerVariablesCore);

    [Test]
    public async Task DeclaredGroupsAndOptionalTracksUseTheSamePreferencesPath()
        => await SampleUiTests.RunAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "galnet-custom-mix-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var localization = CreateLocalization();
                using var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
                using var settings = new AvaloniaPlayerSettingsService(localization, new GamePageViewModel(null!));
                var configuration = new GalNet.Contracts.Builtins.Audio.AudioConfiguration(
                    [new() { Id = "rain/near", BusId = "ambience" }, new() { Id = "wind", BusId = "ambience" }],
                    [new() { Id = "ambience", Name = "Environment" }]);
                await settings.AttachAsync(variables, new(), configuration, showTrackControls: true);
                var items = settings.Categories.Single(category => category.Id == "audio").Items.ToDictionary(item => item.Id);
                await ((DoubleSetting)items["audio.group.ambience.gain"]).TrySetValueAsync(.4);
                await ((DoubleSetting)items["audio.track.rain%2Fnear.gain"]).TrySetValueAsync(.6);
                await ((BoolSetting)items["audio.track.wind.muted"]).TrySetValueAsync(true);
                await settings.FlushAsync();
                var restored = new GameSettings();
                await settings.AttachAsync(variables, restored, configuration);
                Assert.That(restored.Buses["ambience"].Gain, Is.EqualTo(.4f));
                Assert.That(restored.Tracks["rain/near"].Gain, Is.EqualTo(.6f));
                Assert.That(restored.Tracks["wind"].Muted, Is.True);
                Assert.That(settings.Categories.Single(category => category.Id == "audio").Items.Any(item => item.Id.StartsWith("audio.track.", StringComparison.Ordinal)), Is.False);
                var empty = new GameSettings();
                await settings.AttachAsync(variables, empty);
                Assert.That(empty.Buses, Is.Empty);
                Assert.That(empty.Tracks, Is.Empty);
                Assert.That(settings.Categories.Single(category => category.Id == "audio").Items, Has.Count.EqualTo(2));
                Assert.That(GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create().Buses, Has.Count.EqualTo(3));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });

    private static async Task SettingsPersistAndRestoreWithoutLosingOtherPlayerVariablesCore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var localization = CreateLocalization();
            using (var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory)))
            using (var settings = new AvaloniaPlayerSettingsService(localization, new GamePageViewModel(null!)))
            {
                var other = new GalNet.Core.Variable.Variable { Name = "player.other" };
                other.SetValue("preserved");
                variables.NotifyVariableChanged(GalNet.Core.Variable.VariableScope.Player, other.Name, other);
                var runtime = new GameSettings();
                await settings.AttachAsync(variables, runtime, GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create());
                var language = settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<string>>().Single();
                await language.TrySetValueAsync("zh-CN");
                var items = settings.Categories.SelectMany(category => category.Items).ToDictionary(item => item.Id);
                await ((DoubleSetting)items["textSpeed"]).TrySetValueAsync(65);
                await ((IntSetting)items["quickInterval"]).TrySetValueAsync(75);
                await ((IntSetting)items["autoInterval"]).TrySetValueAsync(3250);
                await ((BoolSetting)items["quickReadOnly"]).TrySetValueAsync(false);
                await ((DoubleSetting)items["masterVolume"]).TrySetValueAsync(.37);
                await ((BoolSetting)items["audio.group.vocal.muted"]).TrySetValueAsync(true);
                Assert.That(runtime.QuickReadOnly, Is.False);
                Assert.That(runtime.Buses["vocal"].Muted, Is.True);
                await settings.FlushAsync();
            }
            using var restoredLocalization = CreateLocalization();
            using var restoredVariables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            using var restoredSettings = new AvaloniaPlayerSettingsService(restoredLocalization, new GamePageViewModel(null!));
            var restoredRuntime = new GameSettings();
            await restoredSettings.AttachAsync(restoredVariables, restoredRuntime, GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create());
            Assert.Multiple(() =>
            {
                Assert.That(restoredLocalization.CultureService.CurrentCulture.Name, Is.EqualTo("zh-CN"));
                Assert.That(restoredSettings.Categories.SelectMany(category => category.Items).OfType<DoubleSetting>().Single(item => item.Id == "textSpeed").Value, Is.EqualTo(65));
                Assert.That(restoredRuntime.QuickReadOnly, Is.False);
                Assert.That(restoredRuntime.QuickAdvanceIntervalMilliseconds, Is.EqualTo(75));
                Assert.That(restoredRuntime.AutoAdvanceIntervalMilliseconds, Is.EqualTo(3250));
                Assert.That(restoredVariables.GetSnapshot(GalNet.Core.Variable.VariableScope.Player)["player.galnet.settings.quickInterval"].Type, Is.EqualTo(GalNet.Core.Variable.VariableType.Int));
                Assert.That(restoredRuntime.MasterVolume, Is.EqualTo(.37).Within(.000001));
                Assert.That(restoredRuntime.Buses["vocal"].Muted, Is.True);
                Assert.That(restoredVariables.GetSnapshot(GalNet.Core.Variable.VariableScope.Player)["player.other"].AsString(), Is.EqualTo("preserved"));
            });

            await restoredVariables.ResetPlayerVariablesAsync(CancellationToken.None);
            var defaults = new GameSettings();
            await restoredSettings.AttachAsync(restoredVariables, defaults, GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create());
            Assert.Multiple(() =>
            {
                Assert.That(restoredSettings.RuntimeSettings, Is.SameAs(defaults));
                Assert.That(defaults.TextSpeed, Is.EqualTo(30));
                Assert.That(defaults.MasterVolume, Is.EqualTo(1));
                Assert.That(defaults.Buses["vocal"].Muted, Is.False);
                Assert.That(defaults.QuickReadOnly, Is.True);
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static AvaloniaPlayerLocalization CreateLocalization() => SampleCustomization.CreateLocalizationService();

    [Test]
    public async Task SettingsSnapshotLanguagesAfterContentLoadWithoutWatchingProviders()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-language-options-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var localization = CreateLocalization();
            using var settings = new AvaloniaPlayerSettingsService(localization, new GamePageViewModel(null!));
            localization.AttachGame(new GalNet.GameHost.Content.GameContent
            {
                Graph = new(),
                Localization = new() { ExternalSources = [new("ja-JP", "external", """{"line":"日本語"}""")] }
            });
            using var variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            await settings.AttachAsync(variables, new GameSettings(), GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create());
            var language = settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<string>>().Single();
            Assert.That(language.Choices.Select(option => option.Value), Does.Contain("ja-JP"));
            localization.AttachGame(new GalNet.GameHost.Content.GameContent { Graph = new() });
            Assert.That(language.Choices.Select(option => option.Value), Does.Contain("ja-JP"), "No runtime provider-change refresh.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void ReplacingGameSourcesPreservesEmbeddedUiProviderAndBinding()
    {
        using var localization = CreateLocalization();
        using var title = new LocalizedString(localization.CultureService, "App.Title");
        localization.AttachGame(new GalNet.GameHost.Content.GameContent { Graph = new() });
        localization.AttachGame(new GalNet.GameHost.Content.GameContent { Graph = new() });
        Assert.That(title.Value, Is.EqualTo("GalNet Avalonia Sample Player"));
        localization.CultureService.SetCulture("zh-CN");
        Assert.That(localization["Debug.Menu"], Is.EqualTo("调试"));
    }

    [Test]
    public void ExternalAndCustomSourcesOverrideUiAndGameWithoutLosingOtherKeys()
    {
        using var localization = CreateLocalization();
        using var title = new LocalizedString(localization.CultureService, "App.Title");
        var content = new GalNet.GameHost.Content.GameContent
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
        using var settings = new AvaloniaPlayerSettingsService(localization, new GamePageViewModel(null!));
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
        localization.AttachGame(new GalNet.GameHost.Content.GameContent { Graph = new() });
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
            Assembly = typeof(SampleCustomization).Assembly
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
