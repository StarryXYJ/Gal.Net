using System.Globalization;
using GalNet.Runtime.I18n;

namespace GeneralTest.Runtime;

public sealed class GameTextResolverTests
{
    [Test]
    public void ExactCulturePrecedesParentOverridesAndDefaultCulture()
    {
        var current = CultureInfo.GetCultureInfo("zh-HK");
        var resolver = new GameTextResolver([
            Source("zh", "line", "parent"), Source("zh-HK", "line", "exact"),
            Source("zh", "line", "parent-overlay"), Source("en-US", "line", "default")
        ], CultureInfo.GetCultureInfo("en-US"), () => current);
        Assert.That(resolver.Resolve("line"), Is.EqualTo("exact"));
        current = CultureInfo.GetCultureInfo("zh-CN");
        Assert.That(resolver.Resolve("line"), Is.EqualTo("parent-overlay"));
        current = CultureInfo.GetCultureInfo("ja-JP");
        Assert.That(resolver.Resolve("line"), Is.EqualTo("default"));
        Assert.That(resolver.Resolve("missing"), Is.EqualTo("missing"));
    }

    [Test]
    public void LaterSourcesOverrideOnlyTheirOwnKeysAndTheSourceOrderIsSnapshotted()
    {
        var sources = new List<LocalizedTextSource> { Source("en-US", "line", "base"), Source("en-US", "unique", "kept"), Source("en-US", "line", "overlay") };
        var resolver = new GameTextResolver(sources, CultureInfo.GetCultureInfo("en-US"), () => CultureInfo.GetCultureInfo("en-US"));
        sources.Clear();
        Assert.Multiple(() =>
        {
            Assert.That(resolver.Resolve("line"), Is.EqualTo("overlay"));
            Assert.That(resolver.Resolve("unique"), Is.EqualTo("kept"));
            Assert.That(resolver.AvailableCultures, Has.Count.EqualTo(1));
        });
    }

    private static LocalizedTextSource Source(string culture, string key, string value) =>
        new(CultureInfo.GetCultureInfo(culture), candidate => candidate == key ? value : null);

    [TestCase("")]
    [TestCase(" \t\n")]
    public void EmptyReferencesResolveToEmptyTextWithoutLookingUpAMissingKey(string key)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var resolver = new GameTextResolver([new(culture, _ => throw new AssertionException("Empty references must not query localization sources."))], culture, () => culture);
        Assert.That(resolver.Resolve(key), Is.Empty);
        Assert.That(resolver.TryGetString(key, culture, out var text), Is.True);
        Assert.That(text, Is.Empty);
    }
}
