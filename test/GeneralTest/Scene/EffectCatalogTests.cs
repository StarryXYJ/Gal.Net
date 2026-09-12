using GalNet.Core.Scene;

namespace GeneralTest.Scene;

public class EffectCatalogTests
{
    private static readonly EffectCatalog Catalog = new(
    [
        new EffectDefinition("test.layer", EffectStage.Layer,
            [new("count", EffectParameterKind.Integer, Required: true, Minimum: 1), new("mode", EffectParameterKind.Select, Options: ["A", "B"])],
            [new("progress", AnimationValueKind.Float, 0, 1)])
    ]);

    [Test]
    public void Catalog_reports_editor_diagnostics_without_throwing()
    {
        var diagnostics = Catalog.Validate("test.layer", "", "{\"count\":0,\"mode\":\"C\",\"extra\":true}");

        Assert.That(diagnostics, Has.Count.EqualTo(4));
        Assert.That(diagnostics, Has.Some.Contains("requires targetHandleId"));
        Assert.That(diagnostics, Has.Some.Contains("outside its supported range"));
        Assert.That(diagnostics, Has.Some.Contains("unsupported value"));
        Assert.That(diagnostics, Has.Some.Contains("does not declare parameter"));
    }

    [Test]
    public void Catalog_accepts_valid_effect_metadata_values()
    {
        Assert.That(Catalog.Validate("test.layer", "background", "{\"count\":2,\"mode\":\"A\"}"), Is.Empty);
    }

    [Test]
    public void Catalog_rejects_scene_stage_targets_without_using_a_different_schema()
    {
        var catalog = new EffectCatalog(
        [
            new EffectDefinition("test.scene", EffectStage.ScenePost, [], [])
        ]);

        Assert.That(catalog.Validate("test.scene", "portrait", "{}"), Has.Some.Contains("must not target"));
        Assert.That(catalog.Validate("test.scene", "", "{}"), Is.Empty);
    }
}
