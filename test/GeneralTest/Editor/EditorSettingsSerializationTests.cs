using System.Text.Json;
using GalNet.Core.Settings;

namespace GeneralTest.Editor;

public class EditorSettingsSerializationTests
{
    [Test]
    public void LastDockLayout_RoundTripsAsAString()
    {
        var settings = new EditorSettings { LastDockLayout = "{\n  \"version\": 1\n}" };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<EditorSettings>(json);

        Assert.That(restored?.LastDockLayout, Is.EqualTo(settings.LastDockLayout));
        Assert.That(() => JsonSerializer.Deserialize<EditorSettings>("{\"LastDockLayout\":\"{\\\"version\\\":1}\"}"), Throws.TypeOf<JsonException>());
    }
}
