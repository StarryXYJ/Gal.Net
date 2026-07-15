using GalNet.Editor.Shared.UI;
using Avalonia.Media;
using GalNet.Control.UI;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.UI;
using System.Text.Json;
using System.Reflection;
using GalNet.Control.ViewModels;

namespace GalNet.Control.Tests;

public sealed class UiProjectTests
{
    [Test]
    public async Task UiConfiguration_IsPersistedAsOneDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FileUiProjectProvider(root);
            provider.Current.Title.TitleColor = Color.Parse("#FF123456");
            provider.Current.Game.DialogueHeight = 200;
            await provider.SaveAsync();

            var loaded = new FileUiProjectProvider(root);
            Assert.That(loaded.Current.Title.TitleColor, Is.EqualTo(Color.Parse("#FF123456")));
            Assert.That(loaded.Current.Game.DialogueHeight, Is.EqualTo(200));
            Assert.That(File.Exists(Path.Combine(root, "UI", "ui.json")), Is.True);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void LegacyStringColors_DoNotDiscardTheSavedUiDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "UI"));
            File.WriteAllText(Path.Combine(root, "UI", "ui.json"), """
                { "Title": { "TitleColor": "#FF123456", "TitleFontSize": 72 } }
                """);

            var provider = new FileUiProjectProvider(root);

            Assert.That(provider.Current.Title.TitleColor, Is.EqualTo(Color.Parse("#FF123456")));
            Assert.That(provider.Current.Title.TitleFontSize, Is.EqualTo(72));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task SettingsControlColors_PreserveArgbWhenPersisted()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FileUiProjectProvider(root);
            provider.Current.Settings.BackgroundColor = Color.Parse("#00112233");
            provider.Current.Settings.PanelColor = Color.Parse("#80123456");
            provider.Current.Settings.BackButtonForegroundColor = Color.Parse("#CCABCDEF");
            provider.Current.Settings.SliderTrackColor = Color.Parse("#40556677");
            provider.Current.Settings.SliderFillColor = Color.Parse("#80667788");
            provider.Current.Settings.SliderThumbColor = Color.Parse("#C0778899");
            provider.Current.Settings.CheckBoxBorderColor = Color.Parse("#408899AA");
            provider.Current.Settings.CheckBoxFillColor = Color.Parse("#8099AABB");
            provider.Current.Settings.CheckBoxCheckColor = Color.Parse("#C0AABBCC");
            await provider.SaveAsync();

            var loaded = new FileUiProjectProvider(root).Current.Settings;
            Assert.Multiple(() =>
            {
                Assert.That(loaded.BackgroundColor, Is.EqualTo(Color.Parse("#00112233")));
                Assert.That(loaded.PanelColor, Is.EqualTo(Color.Parse("#80123456")));
                Assert.That(loaded.BackButtonForegroundColor, Is.EqualTo(Color.Parse("#CCABCDEF")));
                Assert.That(loaded.SliderTrackColor, Is.EqualTo(Color.Parse("#40556677")));
                Assert.That(loaded.SliderFillColor, Is.EqualTo(Color.Parse("#80667788")));
                Assert.That(loaded.SliderThumbColor, Is.EqualTo(Color.Parse("#C0778899")));
                Assert.That(loaded.CheckBoxBorderColor, Is.EqualTo(Color.Parse("#408899AA")));
                Assert.That(loaded.CheckBoxFillColor, Is.EqualTo(Color.Parse("#8099AABB")));
                Assert.That(loaded.CheckBoxCheckColor, Is.EqualTo(Color.Parse("#C0AABBCC")));
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void BuiltInPresets_ExposeOnlyRelevantStandardScreenColors()
    {
        var registry = new BuiltInUiPresetRegistry();
        var settings = registry.GetDefault(UiPageKind.Settings).Settings.Select(x => x.Key).ToHashSet();
        var saveLoad = registry.GetDefault(UiPageKind.SaveLoad).Settings.Select(x => x.Key).ToHashSet();
        var gallery = registry.GetDefault(UiPageKind.Gallery).Settings.Select(x => x.Key).ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(settings, Does.Contain("backButtonForegroundColor"));
            Assert.That(settings, Does.Contain("sliderTrackColor"));
            Assert.That(settings, Does.Contain("sliderFillColor"));
            Assert.That(settings, Does.Contain("sliderThumbColor"));
            Assert.That(settings, Does.Contain("checkBoxBorderColor"));
            Assert.That(settings, Does.Contain("checkBoxFillColor"));
            Assert.That(settings, Does.Contain("checkBoxCheckColor"));
            Assert.That(saveLoad, Does.Contain("backButtonForegroundColor"));
            Assert.That(gallery, Does.Contain("backButtonForegroundColor"));
            Assert.That(saveLoad, Does.Not.Contain("sliderTrackColor"));
            Assert.That(gallery, Does.Not.Contain("checkBoxFillColor"));
        });

        var textMenu = registry.GetRequired("builtin.title.text-menu").Settings;
        var hoverScale = textMenu.Single(setting => setting.Key == "hoverScale");
        Assert.Multiple(() =>
        {
            Assert.That(hoverScale.Type, Is.EqualTo(UiSettingType.Float));
            Assert.That(hoverScale.DefaultValue, Is.EqualTo("1.08"));
            Assert.That(hoverScale.Minimum, Is.EqualTo(0.5));
            Assert.That(hoverScale.Maximum, Is.EqualTo(2));
        });
    }

    [Test]
    public void SettingsPresetColors_AreAppliedToRuntimeConfiguration()
    {
        var values = new Dictionary<string, string>
        {
            ["backgroundColor"] = "#10112233",
            ["panelColor"] = "#20123456",
            ["textColor"] = "#30ABCDEF",
            ["buttonColor"] = "#40111111",
            ["buttonTextColor"] = "#50222222",
            ["backButtonForegroundColor"] = "#60333333",
            ["sliderTrackColor"] = "#70444444",
            ["sliderFillColor"] = "#80555555",
            ["sliderThumbColor"] = "#90666666",
            ["checkBoxBorderColor"] = "#A0777777",
            ["checkBoxFillColor"] = "#B0888888",
            ["checkBoxCheckColor"] = "#C0999999"
        };
        var factoryMethod = typeof(GameFlowFactory).GetMethod("CreateSettingsConfiguration", BindingFlags.NonPublic | BindingFlags.Static);

        var configuration = (SettingsUiConfiguration)factoryMethod!.Invoke(null, [new SettingsUiConfiguration(), values])!;

        Assert.Multiple(() =>
        {
            Assert.That(configuration.BackgroundColor, Is.EqualTo(Color.Parse(values["backgroundColor"])));
            Assert.That(configuration.PanelColor, Is.EqualTo(Color.Parse(values["panelColor"])));
            Assert.That(configuration.TextColor, Is.EqualTo(Color.Parse(values["textColor"])));
            Assert.That(configuration.ButtonColor, Is.EqualTo(Color.Parse(values["buttonColor"])));
            Assert.That(configuration.ButtonTextColor, Is.EqualTo(Color.Parse(values["buttonTextColor"])));
            Assert.That(configuration.BackButtonForegroundColor, Is.EqualTo(Color.Parse(values["backButtonForegroundColor"])));
            Assert.That(configuration.SliderTrackColor, Is.EqualTo(Color.Parse(values["sliderTrackColor"])));
            Assert.That(configuration.SliderFillColor, Is.EqualTo(Color.Parse(values["sliderFillColor"])));
            Assert.That(configuration.SliderThumbColor, Is.EqualTo(Color.Parse(values["sliderThumbColor"])));
            Assert.That(configuration.CheckBoxBorderColor, Is.EqualTo(Color.Parse(values["checkBoxBorderColor"])));
            Assert.That(configuration.CheckBoxFillColor, Is.EqualTo(Color.Parse(values["checkBoxFillColor"])));
            Assert.That(configuration.CheckBoxCheckColor, Is.EqualTo(Color.Parse(values["checkBoxCheckColor"])));
        });
    }

    [Test]
    public void BuiltInPresetLocalizationKeys_ExistInEveryEditorLocale()
    {
        var registry = new BuiltInUiPresetRegistry();
        var requiredKeys = Enum.GetValues<UiPageKind>()
            .SelectMany(registry.GetPresets)
            .SelectMany(preset => new[] { preset.Metadata.DisplayNameKey, preset.Metadata.DescriptionKey }
                .Concat(preset.Settings.Select(setting => setting.DisplayNameKey))
                .Concat(preset.Settings.SelectMany(setting => setting.Options ?? []).Select(option => option.DisplayNameKey)))
            .ToHashSet(StringComparer.Ordinal);
        requiredKeys.UnionWith(UiColorPalettePresets.All.Select(palette => palette.DisplayNameKey));
        requiredKeys.UnionWith(["NewProject.ColorPalette", "UiCustomization.ResetPalette", "UiCustomization.ResetPalette.Title", "Common.Cancel", "Common.Confirm"]);
        var localizationDirectory = FindLocalizationDirectory();

        foreach (var culture in new[] { "en-US", "zh-CN", "ja-JP", "ko-KR" })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(localizationDirectory, culture + ".json")));
            var missing = requiredKeys.Where(key => !document.RootElement.TryGetProperty(key, out _)).ToArray();
            Assert.That(missing, Is.Empty, $"{culture} is missing: {string.Join(", ", missing)}");
        }
    }

    [Test]
    public void ColorPalette_UpdatesAllUiColorsAndRetainsNonColorSettings()
    {
        var ui = new UiProject();
        var title = ui.GetPage(UiPageKind.Title);
        title.Settings["titleFontSize"] = "72";
        title.PresetSettings["builtin.title.text-menu"] = new()
        {
            ["hoverScale"] = "1.5",
            ["menuTextColor"] = "#FFFFFFFF"
        };

        UiColorPalettePresets.Apply(ui, "rose-dusk");

        Assert.Multiple(() =>
        {
            Assert.That(ui.ColorPaletteId, Is.EqualTo("rose-dusk"));
            Assert.That(ui.Title.BackgroundColor, Is.EqualTo(Color.Parse("#FF1A1018")));
            Assert.That(ui.Settings.SliderFillColor, Is.EqualTo(Color.Parse("#FFFF8EBC")));
            Assert.That(title.Settings["titleFontSize"], Is.EqualTo("72"));
            Assert.That(title.PresetSettings["builtin.title.text-menu"]["hoverScale"], Is.EqualTo("1.5"));
            Assert.That(title.PresetSettings["builtin.title.text-menu"]["menuTextColor"], Is.EqualTo(Color.Parse("#FFFFF4F8").ToString()));
        });
    }

    [Test]
    public void PageSelection_RetainsSettingsForEachSelectedPreset()
    {
        var selection = new UiPageSelection
        {
            PresetId = "builtin.title.button-menu",
            Settings = new() { ["titleFontSize"] = "64", ["menuItemWidth"] = "320" }
        };

        selection.EnsureActivePresetSettings();
        selection.SwitchPreset("builtin.title.text-menu", new Dictionary<string, string> { ["hoverScale"] = "1.08" });
        selection.Settings["hoverScale"] = "1.4";
        selection.SaveActivePresetSettings();
        selection.SwitchPreset("builtin.title.button-menu", new Dictionary<string, string>());

        Assert.Multiple(() =>
        {
            Assert.That(selection.Settings["titleFontSize"], Is.EqualTo("64"));
            Assert.That(selection.Settings["menuItemWidth"], Is.EqualTo("320"));
        });

        selection.SwitchPreset("builtin.title.text-menu", new Dictionary<string, string>());
        Assert.That(selection.Settings["hoverScale"], Is.EqualTo("1.4"));
    }

    private static string FindLocalizationDirectory()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "GalNet.Editor", "Localization");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate the editor localization directory.");
    }
}
