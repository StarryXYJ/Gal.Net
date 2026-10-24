using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Variants;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    private static ImageVariantsDefinition VariantsDefinition() => ImageVariantsBuilder.Build(new() { Folder = ".", Default = "idle_coat" },
        new[] { "idle_coat", "idle_skirt", "smile_coat", "smile_skirt", "weap" }.Select(key => new ImageVariantFile(key + ".png", "sprite-" + key))).Definition!;
    private static EditorEntryData VariantsShow(string id = "show", string assetId = "atlas", string condition = "") => Entry(id, BuiltinEntryIds.ShowVariants,
        new { handleId = "figure", assetId, selection = new { d1 = "smile", d2 = "skirt" }, transform = new { x = 12 }, opacity = .7 }, condition);
    private static EditorEntryData VariantsSelect(string id, string value) => Entry(id, BuiltinEntryIds.SelectVariants, new { handleId = "figure", dimension = "d1", value });
    private static SceneTargetAnalysis VariantAnalysis(List<EditorEntryData> entries, bool prepared = true) => new(Graph("g"),
        new Dictionary<string, List<EditorEntryData>> { ["g"] = entries }, variants: prepared ? new Dictionary<string, ImageVariantsDefinition?> { ["atlas"] = VariantsDefinition() } : null);

    [Test]
    public void VariantsRetainRequestAcrossFallbackAndKeepContinuousPropertiesAndLocator()
    {
        var entries = new List<EditorEntryData> { VariantsShow(), VariantsSelect("weapon", "weap"), VariantsSelect("return", "smile") };
        var source = JsonSerializer.Serialize(entries);
        var analysis = VariantAnalysis(entries);
        var reduced = analysis.Before("g", "return"); var target = reduced.Targets.Single();
        Assert.That(reduced.Complete, Is.True);
        Assert.That(target.Resource, Is.EqualTo("sprite-weap"));
        Assert.That(target.Variants!.AssetIds, Is.EqualTo(new[] { "atlas" }));
        Assert.That(target.Variants.Selection!.Request["d2"], Is.EqualTo("skirt"));
        Assert.That(target.Variants.Selection.Reason, Is.EqualTo(ImageVariantResolutionReason.Reduced));
        var restored = analysis.Before("g", null).Targets.Single();
        Assert.That(restored.Resource, Is.EqualTo("sprite-smile_skirt"));
        Assert.That(restored.Values["transform.x"], Is.EqualTo(12));
        Assert.That(restored.Values["opacity"], Is.EqualTo(.7f));
        Assert.That(restored.Properties.Select(property => property.Name), Does.Contain("transform.x").And.Not.Contain("flipbook.index"));
        Assert.That(restored.Locator, Is.EqualTo(target.Locator));
        Assert.That(JsonSerializer.Serialize(entries), Is.EqualTo(source));
    }

    [TestCase(BuiltinEntryIds.ReplaceLayer)]
    [TestCase(BuiltinEntryIds.ShowLayer)]
    [TestCase(BuiltinEntryIds.ShowColorLayer)]
    [TestCase(BuiltinEntryIds.PlayVideo)]
    public void OrdinarySourcesClearVariantsIdentity(string type)
    {
        var result = VariantAnalysis([VariantsShow(), Entry("ordinary", type, new { handleId = "figure", assetId = "plain", asset = "movie", color = "#ffffff" })]).Before("g", null);
        Assert.That(result.Targets.Single().Variants, Is.Null);
    }

    [Test]
    public void HideAndReshowResetSelectionWithoutRevivingTheOldIntent()
    {
        var result = VariantAnalysis([VariantsShow(), Entry("hide", BuiltinEntryIds.HideLayer, new { handleId = "figure" }),
            Entry("new", BuiltinEntryIds.ShowVariants, new { handleId = "figure", assetId = "atlas" })]).Before("g", null);
        Assert.That(result.Complete, Is.True);
        Assert.That(result.Targets.Single().Resource, Is.EqualTo("sprite-idle_coat"));
        Assert.That(result.Targets.Single().Locator.EntryId, Is.EqualTo("new"));
    }

    [Test]
    public void UnpreparedDefinitionsKeepLayerCapabilitiesButDoNotInventASpriteOrSelection()
    {
        var result = VariantAnalysis([VariantsShow()], prepared: false).Before("g", null); var target = result.Targets.Single();
        Assert.That(result.Complete, Is.False);
        Assert.That(target.Presence, Is.EqualTo(TargetPresence.Definite));
        Assert.That(target.Resource, Is.Empty);
        Assert.That(target.Variants!.Definite, Is.True);
        Assert.That(target.Variants.Selection, Is.Null);
        Assert.That(target.Properties.Any(property => property.Name == "opacity"), Is.True);
        Assert.That(target.Values["transform.x"], Is.EqualTo(12));
    }

    [TestCase("show")]
    [TestCase("select")]
    [TestCase("unknown")]
    public void InvalidOrUnknownSemanticsNeverRetainAnAuthoritativeVariantsRequest(string kind)
    {
        var invalid = kind switch
        {
            "show" => Entry("bad", BuiltinEntryIds.ShowVariants, new { handleId = "figure", assetId = "atlas", selection = new { missing = "value" } }),
            "select" => VariantsSelect("bad", "not-an-option"),
            _ => Entry("bad", "custom.scene", new { })
        };
        var result = VariantAnalysis([VariantsShow(), invalid]).Before("g", null);
        Assert.That(result.Complete, Is.False);
        Assert.That(result.Targets.Single().Variants, Is.Null);
        if (kind != "unknown") Assert.That(result.Targets.Single().Resource, Is.Empty);
    }

    [Test]
    public void SelectingAPlainLayerIsIncompleteAndDoesNotAlterItsOrdinaryFacts()
    {
        var result = VariantAnalysis([Layer("plain", "figure"), VariantsSelect("bad", "weap")]).Before("g", null);
        Assert.That(result.Complete, Is.False);
        Assert.That(result.Targets.Single().Resource, Is.EqualTo("same-image"));
        Assert.That(result.Targets.Single().Variants, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BranchMergesKeepCandidatesButOnlyPublishCertainAtlasIdentityAndSelection(bool differentAtlas)
    {
        var graph = Graph("root", "left", "right", "merge"); Edge(graph, "root", "left"); Edge(graph, "root", "right"); Edge(graph, "left", "merge"); Edge(graph, "right", "merge");
        var groups = new Dictionary<string, List<EditorEntryData>>
        {
            ["root"] = [VariantsShow()],
            ["left"] = [VariantsSelect("select", "weap")],
            ["right"] = differentAtlas ? [VariantsShow("other", "second")] : [],
            ["merge"] = [VariantsSelect("next", "idle")]
        };
        var definitions = new Dictionary<string, ImageVariantsDefinition?> { ["atlas"] = VariantsDefinition(), ["second"] = VariantsDefinition() };
        var analysis = new SceneTargetAnalysis(graph, groups, variants: definitions); var target = analysis.Before("merge", "next").Targets.Single();
        Assert.That(target.Presence, Is.EqualTo(TargetPresence.Definite));
        Assert.That(target.Variants!.Definite, Is.EqualTo(!differentAtlas));
        Assert.That(target.Variants.AssetIds.Count, Is.EqualTo(differentAtlas ? 2 : 1));
        Assert.That(target.Variants.Selection, Is.Null);
        Assert.That(target.Resource, Is.Empty);
        var after = analysis.Before("merge", null);
        Assert.That(after.Complete, Is.False);
        Assert.That(after.Targets.Single().Variants!.AssetIds, Is.EqualTo(target.Variants.AssetIds));
        Assert.That(after.Targets.Single().Values["transform.x"], Is.EqualTo(12));
    }

    [Test]
    public void ConditionalReplacementKeepsAtlasOnlyAsAPossibility()
    {
        var result = VariantAnalysis([VariantsShow(), Entry("replace", BuiltinEntryIds.ReplaceLayer, new { handleId = "figure", assetId = "plain" }, "save.flag")]).Before("g", null);
        Assert.That(result.Targets.Single().Variants!.Definite, Is.False);
        Assert.That(result.Targets.Single().Variants!.Selection, Is.Null);
        Assert.That(result.Targets.Single().Resource, Is.Empty);
    }

    [Test]
    public void EquivalentRequestsConvergeAcrossLoopsAndTimelineSamplesTheVariantsLayer()
    {
        var graph = Graph("start", "loop"); Edge(graph, "start", "loop"); Edge(graph, "loop", "loop");
        var timeline = Entry("timeline", BuiltinEntryIds.PlayTimeline, new { timeline = new { playbackHandleId = "play", durationFrames = 10, blocking = true, tracks = new[] { new { handleId = "figure", property = "transform.x", keys = new[] { new { frame = 0, value = 12 }, new { frame = 10, value = 22 } } } }, events = Array.Empty<object>() } });
        var groups = new Dictionary<string, List<EditorEntryData>> { ["start"] = [VariantsShow()], ["loop"] = [VariantsShow("again"), timeline] };
        var analysis = new SceneTargetAnalysis(graph, groups, variants: new Dictionary<string, ImageVariantsDefinition?> { ["atlas"] = VariantsDefinition() });
        var result = analysis.AtTimeline("loop", "timeline", new(5));
        Assert.That(result.Complete, Is.True);
        Assert.That(result.Targets.Single().Values["transform.x"], Is.EqualTo(17));
        Assert.That(result.Targets.Single().Variants!.Selection!.Variant.Key, Is.EqualTo("smile_skirt"));
    }

    [Test]
    public void CopyRemapsVariantsCreationAndSelectionButPreservesResourceAndDimensionStrings()
    {
        var show = VariantsShow(); var select = VariantsSelect("select", "weap");
        SceneAuthoringIdentities.RemapCopy([show, select]);
        Assert.That(show.Parameters["handleId"].GetString(), Is.Not.EqualTo("figure"));
        Assert.That(select.Parameters["handleId"].GetString(), Is.EqualTo(show.Parameters["handleId"].GetString()));
        Assert.That(show.Parameters["assetId"].GetString(), Is.EqualTo("atlas"));
        Assert.That(select.Parameters["dimension"].GetString(), Is.EqualTo("d1"));
        Assert.That(select.Parameters["value"].GetString(), Is.EqualTo("weap"));
        Assert.That(SceneAuthoringIdentities.IsOutput(BuiltinEntryIds.ShowVariants, "handleId"), Is.True);
    }
}
