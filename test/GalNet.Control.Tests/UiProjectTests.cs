using System.Text.Json;
using GalNet.Core.UI;
using GalNet.Editor.Shared.UI;
using NUnit.Framework;

namespace GalNet.Control.Tests;

[TestFixture]
public sealed class UiProjectTests
{
    [Test]
    public void DefaultProject_UsesVersionThreeAndPageSelectionsOnly()
    {
        var project = new UiProject();
        Assert.That(project.Version, Is.EqualTo(3));
        Assert.That(project.Pages.Keys, Is.EquivalentTo(Enum.GetValues<UiPageKind>()));
        Assert.That(JsonSerializer.Serialize(project), Does.Not.Contain("\"BackgroundColor\""));
    }

    [Test]
    public void Provider_RejectsRetiredTypedConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "UI"));
            File.WriteAllText(Path.Combine(root, "UI", "ui.json"), """{ "Version": 2, "Title": {} }""");
            var provider = new FileUiProjectProvider(root);
            Assert.That(provider.Current.Version, Is.EqualTo(3));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void Palette_WritesPageSettings()
    {
        var project = new UiProject();
        UiColorPalettePresets.Apply(project, "rose-dusk");
        Assert.That(project.ColorPaletteId, Is.EqualTo("rose-dusk"));
        Assert.That(project.GetPage(UiPageKind.Title).Settings["backgroundColor"], Is.EqualTo("#FF1A1018").IgnoreCase);
        Assert.That(project.GetPage(UiPageKind.Game).Settings["dialogueTextColor"], Is.EqualTo("#FFFFF4F8").IgnoreCase);
    }
}
