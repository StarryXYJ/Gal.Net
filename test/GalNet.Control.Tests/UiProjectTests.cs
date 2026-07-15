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
}
