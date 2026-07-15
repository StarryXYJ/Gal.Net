using GalNet.Editor.Shared.UI;
using Avalonia.Media;

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
}
