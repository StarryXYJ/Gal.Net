using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor.Shared;

public sealed class LocalizationExchangeTests
{
    [TestCase("{\"a\":\"x\",\"a\":\"y\"}")]
    [TestCase("{\"a\":42}")]
    [TestCase("[]")]
    [TestCase("{\"version\":2,\"projectId\":\"bad\",\"rows\":[]}")]
    public void ExchangeMalformedOrDuplicateJsonIsRejected(string json) => Assert.Catch(() => LocalizationExchange.Parse(json, "en-US"));

    [Test]
    public void ExchangeBatchLeavesPreserveNestedValuesAndRejectObjectCollisions()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("""{"nested":{"line":"old","keep":"keep"}}""");
        var result = LocalizationJson.SetTexts(bytes, new Dictionary<string, string> { ["nested.line"] = "new", ["flat"] = "flat" });
        var values = LocalizationJson.Texts(result);
        Assert.That(values["nested.line"], Is.EqualTo("new")); Assert.That(values["nested.keep"], Is.EqualTo("keep")); Assert.That(values["flat"], Is.EqualTo("flat"));
        Assert.Catch(() => LocalizationJson.SetTexts(bytes, new Dictionary<string, string> { ["nested"] = "cannot replace object" }));
    }

    [Test]
    public void ExchangeTemplateValidatesStableIdentitySourceProofAndDuplicateRows()
    {
        var row = new LocalizationExchangeRow("key", "原文", LocalizationTextState.Fingerprint("原文"), ["group/entry/content"], "Translation");
        var document = new LocalizationExchangeDocument(1, Guid.NewGuid().ToString("N"), "zh-CN", "en-US", [row], true);
        var parsed = LocalizationExchange.Parse(LocalizationExchange.Write(document), "ja-JP");
        Assert.That(parsed.TargetLocale, Is.EqualTo("en-US")); Assert.That(parsed.Rows[0].Key, Is.EqualTo("key"));
        Assert.Catch(() => LocalizationExchange.Parse(LocalizationExchange.Write(document with { Rows = [row, row] }), "en-US"));
        Assert.Catch(() => LocalizationExchange.Parse(LocalizationExchange.Write(document with { Rows = [row with { SourceText = "Changed without proof" }] }), "en-US"));
    }
    [Test]
    public void ExchangeFlatNullEmptyAndVersionNamedTextRemainDistinct()
    {
        var input = LocalizationExchange.Parse("""{"missing":null,"empty":"","version":"text"}""", "en-US");
        Assert.That(input.ProjectId, Is.Null); Assert.That(input.Rows[0].Translation, Is.Null);
        Assert.That(input.Rows[1].Translation, Is.Empty); Assert.That(input.Rows[2].Key, Is.EqualTo("version"));
    }
    [TestCase("[]")]
    [TestCase("{\"version\":1,\"characterHighWater\":0,\"exchangeProjectId\":\"bad\"}")]
    public void ExchangeMetadataIdentityMustBeCurrentGuid(string json) => Assert.Catch(() => LocalizationJson.Validate(LocalizationJson.MetadataPath, System.Text.Encoding.UTF8.GetBytes(json)));
}
