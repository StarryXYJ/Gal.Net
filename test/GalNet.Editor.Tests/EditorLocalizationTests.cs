using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GalNet.Core.I18n;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

[NonParallelizable]
public sealed class EditorLocalizationTests
{
    [Test]
    public void FourPublishedLanguagesHaveIdenticalKeysAndCompatibleFormatArguments()
    {
        var assembly = typeof(EditorLocalizationService).Assembly;
        var maps = new[] { "zh-CN", "en-US", "ja-JP", "ko-KR" }.Select(locale =>
        {
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("Localization." + locale + ".json", StringComparison.Ordinal)))!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }).ToArray();
        foreach (var map in maps)
        {
            Assert.That(map.Keys, Is.EquivalentTo(maps[0].Keys));
            foreach (var (key, value) in map)
            {
                Assert.That(value, Is.Not.Empty, key);
                var actual = CompositeFormat.Parse(value);
                var expected = CompositeFormat.Parse(maps[0][key]);
                Assert.That(actual.MinimumArgumentCount, Is.EqualTo(expected.MinimumArgumentCount), key);
                Assert.That(Regex.Matches(value, @"(?<!\{)\{(\d+)").Select(match => match.Groups[1].Value),
                    Is.EquivalentTo(Regex.Matches(maps[0][key], @"(?<!\{)\{(\d+)").Select(match => match.Groups[1].Value)), key);
            }
        }
    }

    [Test]
    public void LocalePreferenceSurvivesReloadAndUnknownLocalesAndKeysHaveVisibleFallback()
    {
        var current = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-localization-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "editor.json");
            var settings = new EditorSettingsService(path);
            settings.GetSettings().UiLocale = new I18nLocale("ko-KR");
            settings.SaveSettings();
            var localization = new EditorLocalizationService();
            localization.ApplyLocale(new EditorSettingsService(path).GetSettings().UiLocale.Code);
            Assert.That(localization.CurrentCulture.Name, Is.EqualTo("ko-KR"));
            Assert.That(localization["Close.Title"], Is.EqualTo("프로젝트 닫기"));
            localization.ApplyLocale("en-GB");
            Assert.That(localization.CurrentCulture.Name, Is.EqualTo("en-US"));
            localization.ApplyLocale("not a culture!");
            Assert.That(localization.CurrentCulture.Name, Is.EqualTo("zh-CN"));
            Assert.That(localization["Missing.Custom.Key"], Is.EqualTo("[Missing.Custom.Key]"));
            Assert.That(localization.Format("Close.Question", "name/{0}/名称"), Does.Contain("name/{0}/名称"));
        }
        finally { CultureInfo.CurrentCulture = current; CultureInfo.CurrentUICulture = ui; Directory.Delete(root, true); }
    }

    [Test]
    public void FoundationViewsHaveNoUnlistedLiteralUserInterfaceText()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent!;
        var files = new[] { "Views/NewProjectPanelView.axaml", "Views/ProjectSettingsPanelView.axaml", "Views/EditorSettingsPanelView.axaml", "Views/LogPanelView.axaml", "Controls/GraphNodeControl.axaml", "Inspector/Views/NodeInspectorControl.axaml" };
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "×", "bgm / sfx / vocal", "zh-CN" };
        foreach (var file in files)
        {
            var text = File.ReadAllText(Path.Combine(root.FullName, "src/Editor/GalNet.Editor", file));
            foreach (Match match in Regex.Matches(text, "(?:Text|Content|Header|Title|PlaceholderText)=\"([^\"{]+)\""))
                Assert.That(allowed.Contains(match.Groups[1].Value), Is.True, file + ": " + match.Value);
        }
    }
}
