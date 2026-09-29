using System.Text.Json;
using GalNet.Core.Settings;

namespace GeneralTest.Editor;

public class EditorSettingsSerializationTests
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Test]
    public void LastDockLayout_RoundTripsAsAString()
    {
        var settings = new EditorSettings { LastDockLayout = "{\n  \"version\": 1\n}" };

        var json = JsonSerializer.Serialize(settings, IndentedJson);
        var restored = JsonSerializer.Deserialize<EditorSettings>(json);

        Assert.That(restored?.LastDockLayout, Is.Not.Null);
        using var expected = JsonDocument.Parse(settings.LastDockLayout);
        using var actual = JsonDocument.Parse(restored!.LastDockLayout!);
        Assert.That(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), Is.True);
        Assert.That(() => JsonSerializer.Deserialize<EditorSettings>("{\"LastDockLayout\":\"{\\\"version\\\":1}\"}"), Throws.TypeOf<JsonException>());
    }
}
