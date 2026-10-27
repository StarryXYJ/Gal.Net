using System.Text.Json;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Scene;

namespace GeneralTest.Scene;

public sealed class EffectParameterValidationTests
{
    [TestCase("null"), TestCase("\"0.5\""), TestCase("1e1000")]
    public void BothEffectCatalogsRejectNonNumericNullOrUnrepresentableFloatValues(string raw)
    {
        var visual = new EffectCatalog([new("visual", EffectStage.ScenePost, [new("gain", EffectParameterKind.Float)], [])]);
        var audio = new SEffectCatalog([new("audio", new HashSet<SEffectScope> { SEffectScope.Track }, [new("gain")])]);
        using var value = JsonDocument.Parse("{\"gain\":" + raw + "}");
        Assert.That(visual.Validate("visual", "", value.RootElement.GetRawText()), Is.Not.Empty);
        Assert.That(audio.Validate("audio", SEffectScope.Track, value.RootElement), Is.Not.Empty);
    }
    [Test]
    public void CatalogMetadataAndValidationRemainConsistentAfterCallerCollectionsChange()
    {
        var options = new List<string> { "A" }; var parameters = new List<EffectParameterDefinition> { new("mode", EffectParameterKind.Select, true, Options: options) };
        var visual = new EffectCatalog([new("visual", EffectStage.ScenePost, parameters, [])]);
        options.Add("B"); parameters.Clear();
        Assert.That(visual.Validate("visual", "", "{\"mode\":\"B\"}"), Is.Not.Empty);
        Assert.That(visual.Definitions.Single().Parameters.Single().Options, Is.EqualTo(new[] { "A" }));
        var scopes = new HashSet<SEffectScope> { SEffectScope.Track };
        var audio = new SEffectCatalog([new("audio", scopes, [new("gain", true, 0, 1)])]); scopes.Clear();
        Assert.That(audio.Validate("audio", SEffectScope.Track, JsonSerializer.SerializeToElement(new { gain = .5f })), Is.Empty);
        Assert.That(audio.Definitions.Single().SupportedScopes, Does.Contain(SEffectScope.Track));
    }
}
