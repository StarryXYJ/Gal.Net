using Avalonia.Controls;
using Avalonia.Data;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Sample.Avalonia;
using GalNet.Sample.Avalonia.Services;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

[TestFixture]
public sealed class SampleLocalizationTests
{
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
                settings.Attach(variables);
                var language = settings.Items.OfType<ChoicePlayerSetting>().Single();
                language.SelectedOption = language.Options.Single(option => option.Value == "zh-CN");
                settings.Items.OfType<RangePlayerSetting>().Single().Value = 65;
                await settings.FlushAsync();
            }
            using var restoredLocalization = CreateLocalization();
            using var restoredVariables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(directory));
            using var restoredSettings = new SamplePlayerSettingsService(restoredLocalization, new GamePageViewModel(null!));
            restoredSettings.Attach(restoredVariables);
            Assert.Multiple(() =>
            {
                Assert.That(restoredLocalization.CultureService.CurrentCulture.Name, Is.EqualTo("zh-CN"));
                Assert.That(restoredSettings.Items.OfType<RangePlayerSetting>().Single().Value, Is.EqualTo(65));
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
        return new SampleLocalizationService(culture);
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
