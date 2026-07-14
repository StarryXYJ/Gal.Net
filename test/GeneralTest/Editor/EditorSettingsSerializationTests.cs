using System.Text.Json;
using GalNet.Core.Settings;

namespace GeneralTest.Editor;

public class EditorSettingsSerializationTests
{
    [Test]
    public void LastDockLayout_IsWrittenAsReadableJsonAndReadsLegacyStrings()
    {
        var settings = new EditorSettings { LastDockLayout = "{\n  \"version\": 1\n}" };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<EditorSettings>(json);
        var legacy = JsonSerializer.Deserialize<EditorSettings>("{\"LastDockLayout\":\"{\\\"version\\\":1}\"}");

        Assert.That(json, Does.Contain("\"LastDockLayout\": {"));
        Assert.That(json, Does.Not.Contain("\\\\\"version\\\\\""));
        Assert.That(restored?.LastDockLayout, Does.Contain("\"version\""));
        Assert.That(legacy?.LastDockLayout, Is.EqualTo("{\"version\":1}"));
    }
}
