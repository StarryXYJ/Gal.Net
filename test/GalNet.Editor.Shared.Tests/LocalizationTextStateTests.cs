using System.Text;
using System.Text.Json;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class LocalizationTextStateTests
{
    [Test]
    public void SourceRevisionPreservesUnknownMetadataAndRequiresReviewWithoutChangingTranslations()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"version":1,"characterHighWater":17,"keep":{"x":2}}""");
        var result = LocalizationTextState.Update(bytes, "line", "zh-CN", "原文", "新原文", ["en-US"]);
        LocalizationJson.Validate(LocalizationJson.MetadataPath, result);
        Assert.That(LocalizationTextState.NeedsReview(result, "line", "en-US"), Is.True);
        using var json = JsonDocument.Parse(result); Assert.That(json.RootElement.GetProperty("keep").GetProperty("x").GetInt32(), Is.EqualTo(2));
        Assert.That(json.RootElement.GetProperty("characterHighWater").GetInt32(), Is.EqualTo(17));
    }

    [Test]
    public void EmptySourceHasFingerprintAndUnknownTranslationReviewStaysUnknown()
    {
        var result = LocalizationTextState.Update(LocalizationJson.Initial(LocalizationJson.MetadataPath), "line", "zh-CN", null, "", ["en-US"]);
        LocalizationJson.Validate(LocalizationJson.MetadataPath, result);
        Assert.That(LocalizationTextState.NeedsReview(result, "line", "en-US"), Is.Null);
        using var json = JsonDocument.Parse(result);
        Assert.That(json.RootElement.GetProperty("textStates").GetProperty("line").GetProperty("sourceFingerprint").GetString(), Is.EqualTo(LocalizationTextState.Fingerprint("")));
    }

    [TestCase("42")]
    [TestCase("{\"line\":{\"sourceLocale\":\"zh-CN\",\"sourceFingerprint\":\"bad\",\"translations\":{}}}")]
    public void InvalidRevisionMetadataIsRejected(string states)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"version\":1,\"characterHighWater\":0,\"textStates\":" + states + "}");
        Assert.That(() => LocalizationJson.Validate(LocalizationJson.MetadataPath, bytes), Throws.TypeOf<InvalidDataException>());
    }
}
