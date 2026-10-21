using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    private static ShaderEffectDescriptor EffectDescriptor(EffectStage stage = EffectStage.ScenePost) => new(new("program"), 1, "source", new HashSet<EffectStage> { stage },
        [new("amount", "uAmount", ShaderEffectParameterKind.Float, "0.25", 0, 1, Animatable: true),
         new("count", "uCount", ShaderEffectParameterKind.Integer, "2", 0, 10, Animatable: true),
         new("fixed", "uFixed", ShaderEffectParameterKind.Float, "5", Animatable: false),
         new("color", "uColor", ShaderEffectParameterKind.Color, Animatable: true)]);
    private static SceneTargetAnalysis AnalyzeEffects(ShaderEffectDescriptor? descriptor, params EditorEntryData[] entries) =>
        new(Graph("g"), new Dictionary<string, List<EditorEntryData>> { ["g"] = entries.ToList() },
            programs: new Dictionary<string, ShaderEffectDescriptor?> { ["program"] = descriptor });

    [Test]
    public void EffectMetadataPublishesOnlyScalarAnimationPropertiesWithStaticDefaultAndClampedInitials()
    {
        var entry = Entry("effect", BuiltinEntryIds.ApplyEffect, new { instanceId = "effect", program = "program", id = "ignored", parameters = new { amount = 4 } });
        var original = JsonSerializer.Serialize(entry);
        var result = AnalyzeEffects(EffectDescriptor(), entry).Before("g", null); var target = result.Targets.Single();
        Assert.That(result.Complete, Is.True); Assert.That(target.Resource, Is.EqualTo("program"));
        Assert.That(target.Properties.Select(property => property.Name), Is.EquivalentTo(new[] { "progress", "amount", "count" }));
        Assert.That(target.Values["amount"], Is.EqualTo(1)); Assert.That(target.Values["count"], Is.EqualTo(2)); Assert.That(target.Values["progress"], Is.Zero);
        Assert.That(target.Properties.Single(property => property.Name == "amount"), Is.EqualTo(ShaderEffectAnimationProperties.Create(EffectDescriptor())[0]));
        Assert.That(JsonSerializer.Serialize(entry), Is.EqualTo(original));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void MissingOrWrongStageMetadataKeepsIdentityAndIntrinsicCapabilityButNeverInventsShaderProperties(bool wrongStage)
    {
        var result = AnalyzeEffects(wrongStage ? EffectDescriptor(EffectStage.Layer) : null,
            Entry("fx", BuiltinEntryIds.ApplyEffect, new { instanceId = "fx", program = "program" })).Before("g", null);
        Assert.That(result.Complete, Is.False); var target = result.Targets.Single();
        Assert.That(target.Presence, Is.EqualTo(TargetPresence.Definite)); Assert.That(target.Properties.Select(property => property.Name), Is.EqualTo(new[] { "progress" }));
    }
    [Test]
    public void LegacyCatalogIsDistinctFromProgramAndInvalidStaticValuesRemainUnknown()
    {
        var definition = new EffectDefinition("legacy", EffectStage.Layer, [], [new("strength", AnimationValueKind.Float, -1, 1, .5f)]);
        var catalog = new EffectCatalog([definition]);
        var fx = Entry("fx", BuiltinEntryIds.ApplyEffect, new { instanceId = "fx", id = "legacy", program = "", targetHandleId = "layer", parameters = new { strength = -.75f } });
        var analysis = new SceneTargetAnalysis(Graph("g"), new Dictionary<string, List<EditorEntryData>> { ["g"] = [fx] }, effects: catalog);
        var result = analysis.Before("g", null); Assert.That(result.Complete, Is.True);
        Assert.That(result.Targets.Single().Resource, Is.EqualTo("legacy")); Assert.That(result.Targets.Single().Values["strength"], Is.EqualTo(-.75f));
        fx.Parameters["parameters"] = JsonSerializer.SerializeToElement(new { strength = "invalid" });
        result = analysis.Before("g", null); Assert.That(result.Complete, Is.False);
        Assert.That(result.Targets.Single().Properties.Any(property => property.Name == "strength"), Is.True); Assert.That(result.Targets.Single().Values.ContainsKey("strength"), Is.False);
    }
    [Test]
    public void EffectEventCapabilitiesAndMetadataBaselineFeedSharedTimelineSampling()
    {
        var clip = Clip([Event(5, BuiltinEntryIds.ApplyEffect, new { instanceId = "fx", program = "program", parameters = new { amount = .3f } }, "fx-event")],
            [new { handleId = "fx", property = "amount", blendMode = "Additive", keys = new[] { new { frame = 0, value = 0f }, new { frame = 20, value = .4f } } }]);
        var analysis = AnalyzeEffects(EffectDescriptor(), clip);
        var future = analysis.AtTimeline("g", "clip", new(0, IncludeFuture: true)).Targets.Single();
        Assert.That(future.IsFuture, Is.True); Assert.That(future.Values, Is.Empty); Assert.That(future.Properties.Any(property => property.Name == "amount"), Is.True);
        var current = analysis.AtTimeline("g", "clip", new(10)).Targets.Single();
        Assert.That(current.Locator, Is.EqualTo(future.Locator)); Assert.That(current.Values.ContainsKey("amount"), Is.False, "An event-created additive baseline remains conservative during playback.");
        Assert.That(analysis.Before("g", null).Targets.Single().Values["amount"], Is.EqualTo(.7f).Within(.00001));
    }
    [Test]
    public void RemovedIncompleteEffectDoesNotPoisonLaterSceneFactsAndProgressUsesCoreInitialValue()
    {
        var fx = Entry("fx", BuiltinEntryIds.ApplyEffect, new { instanceId = "fx", program = "program" });
        var result = AnalyzeEffects(null, fx, Entry("stop", BuiltinEntryIds.StopEffect, new { instanceId = "fx" }), Layer("layer", "layer")).Before("g", null);
        Assert.That(result.Complete, Is.True); Assert.That(result.Targets.Single().Handle, Is.EqualTo("layer"));
        var descriptor = EffectDescriptor() with { Parameters = [new("progress", "uProgress", ShaderEffectParameterKind.Float, "1", Animatable: true)] };
        result = AnalyzeEffects(descriptor, fx).Before("g", null);
        Assert.That(result.Complete, Is.True); Assert.That(result.Targets.Single().Values["progress"], Is.Zero, "Core progress precedes the shader default.");
        descriptor = descriptor with { Parameters = [new("progress", "uProgress", ShaderEffectParameterKind.Float, "1", .5, 1, Animatable: true)] };
        result = AnalyzeEffects(descriptor, fx).Before("g", null);
        Assert.That(result.Complete, Is.False); Assert.That(result.Targets.Single().Values.ContainsKey("progress"), Is.False);
    }
    [Test]
    public void StoppingAnIncompleteEffectInsideTimelineRecoversOnlyCapabilityCompleteness()
    {
        var fx = Entry("fx", BuiltinEntryIds.ApplyEffect, new { instanceId = "fx", program = "program" });
        var clip = Clip([Event(5, BuiltinEntryIds.StopEffect, new { instanceId = "fx" })]);
        var analysis = AnalyzeEffects(null, fx, clip);
        Assert.That(analysis.AtTimeline("g", "clip", new(0)).Complete, Is.False);
        Assert.That(analysis.AtTimeline("g", "clip", new(10)).Complete, Is.True);
        analysis = AnalyzeEffects(null, Entry("unknown", "custom.unknown", new { }), fx, clip);
        Assert.That(analysis.AtTimeline("g", "clip", new(10)).Complete, Is.False, "Removing incomplete capabilities must not erase an upstream semantic uncertainty.");
    }
}
