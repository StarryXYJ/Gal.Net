using System.Text.Json;
using GalNet.Contracts.Builtins.Audio;

namespace GalNet.Core.Tests.Audio;

[TestFixture]
public sealed class SEffectCatalogTests
{
    private static readonly SEffectDefinition LowPass = new(
        "seffect.lowPass",
        new HashSet<SEffectScope> { SEffectScope.Track },
        [new("cutoffHz", Required: true, Minimum: 20, Maximum: 20_000), new("attenuationDb", Minimum: -20, Maximum: 0)]);

    [Test]
    public void Validates_scope_required_and_numeric_bounds()
    {
        var catalog = new SEffectCatalog([LowPass]);
        using var document = JsonDocument.Parse("{\"cutoffHz\": 30, \"attenuationDb\": -12}");

        Assert.That(catalog.Validate("seffect.lowPass", SEffectScope.Track, document.RootElement), Is.Empty);

        using var invalid = JsonDocument.Parse("{\"cutoffHz\": 4}");
        Assert.That(catalog.Validate("seffect.lowPass", SEffectScope.Track, invalid.RootElement), Does.Contain("SEffect parameter 'cutoffHz' is outside its supported range."));
        Assert.That(catalog.Validate("seffect.lowPass", SEffectScope.Item, document.RootElement), Does.Contain("SEffect 'seffect.lowPass' does not support Item scope."));
    }

    [Test]
    public void Rejects_unknown_parameters_and_effects()
    {
        var catalog = new SEffectCatalog([LowPass]);
        using var document = JsonDocument.Parse("{\"cutoffHz\": 800, \"resonance\": 1}");

        Assert.That(catalog.Validate("seffect.lowPass", SEffectScope.Track, document.RootElement), Does.Contain("SEffect 'seffect.lowPass' does not declare parameter 'resonance'."));
        Assert.That(catalog.Validate("seffect.unknown", SEffectScope.Track, document.RootElement), Does.Contain("Unsupported SEffect 'seffect.unknown'."));
    }
}
