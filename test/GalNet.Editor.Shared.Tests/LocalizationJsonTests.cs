using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class LocalizationJsonTests
{
    [Test]
    public void PatchingNestedTextPreservesOtherLeavesUnicodeAndEncoding()
    {
        var encoding = new UnicodeEncoding(false, true, true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("""{"a":{"b":"旧"},"keep":"保留","flat.name":"平铺"}""")).ToArray();
        var after = LocalizationJson.SetText(bytes, "a.b", "新\n文本");
        Assert.That(after.Take(2), Is.EqualTo(encoding.GetPreamble()));
        var text = LocalizationJson.Decode(after);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("a").GetProperty("b").GetString(), Is.EqualTo("新\n文本"));
        Assert.That(LocalizationJson.Text(after, "keep"), Is.EqualTo("保留"));
        Assert.That(LocalizationJson.Text(after, "flat.name"), Is.EqualTo("平铺"));
        Assert.That(LocalizationJson.Text(after, "absent"), Is.Null);
        Assert.That(LocalizationJson.Text(LocalizationJson.SetText(after, "flat.name", ""), "flat.name"), Is.Empty);
    }

    [TestCase("{\"same\":\"a\",\"same\":\"b\"}")]
    [TestCase("{\"a.b\":\"a\",\"a\":{\"b\":\"b\"}}")]
    [TestCase("{\"bad\":42}")]
    public void InvalidTranslationsAreRejectedBeforePatching(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.That(() => LocalizationJson.SetText(bytes, "line", "new"), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void CatalogAndAuthorMetadataKeepUnknownPropertiesAndValidateIdentity()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"version":1,"characterHighWater":17,"extension":{"keep":true}}""");
        var node = JsonNode.Parse(LocalizationJson.Decode(bytes))!;
        node["characterHighWater"] = 18;
        var after = LocalizationJson.WriteLike(bytes, node);
        LocalizationJson.Validate(LocalizationJson.MetadataPath, after);
        Assert.That(JsonNode.Parse(LocalizationJson.Decode(after))!["extension"]!["keep"]!.GetValue<bool>(), Is.True);
        LocalizationJson.Validate(LocalizationJson.CatalogPath, Encoding.UTF8.GetBytes("""[{"id":"1","displayNameKey":"name","extension":42}]"""));
        Assert.That(() => LocalizationJson.Validate(LocalizationJson.CatalogPath, Encoding.UTF8.GetBytes("""[{"id":"1","displayNameKey":"a"},{"id":"1","displayNameKey":"b"}]""")), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("I18n/External/zh-CN/custom.json")]
    [TestCase("I18n/catalog/arbitrary.json")]
    [TestCase("Graph/graph.json")]
    [TestCase("settings.json")]
    public void DedicatedEntryPointCannotManageArbitraryProtectedFiles(string path) =>
        Assert.That(() => LocalizationJson.Path(path), Throws.Exception);
}
