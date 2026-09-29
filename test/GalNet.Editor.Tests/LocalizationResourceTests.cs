using System.Text.Json;
using GalNet.Editor.Services;

namespace GeneralTest.Editor;

[TestFixture]
public sealed class LocalizationResourceTests
{
    [Test]
    public void EveryLocaleIsValidJsonAndHasTheSameKeys()
    {
        var assembly = typeof(EditorLocalizationService).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.Contains("Localization", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name)
            .ToList();
        Assert.That(resources, Has.Count.EqualTo(4));

        HashSet<string>? expected = null;
        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.That(stream, Is.Not.Null, resource);
            using var document = JsonDocument.Parse(stream!);
            var keys = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            expected ??= keys;
            Assert.That(keys, Is.EquivalentTo(expected), resource);
        }
    }
}
