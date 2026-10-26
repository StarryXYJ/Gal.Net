using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;
using SceneLayer = GalNet.Contracts.Builtins.Scene.Layer;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    private static object Sheet(float index = 2.5f) => new { Columns = 3, Rows = 2, FrameCount = 5, Index = index, future = new { number = 9007199254740993L } };
    [TestCase(2.5f)]
    [TestCase(100.25f)]
    public void FlipbookPublishesCoreCapabilityAndRawIndexAndPreservesItAcrossMoveAndReplace(float index)
    {
        var layer = Entry("sheet", BuiltinEntryIds.ShowLayer, new { handleId = "sheet", assetId = "sprite", flipbook = Sheet(index) });
        var move = Entry("move", BuiltinEntryIds.MoveLayer, new { handleId = "sheet", transform = new { x = 9 } });
        var replace = Entry("replace", BuiltinEntryIds.ReplaceLayer, new { handleId = "sheet", assetId = "other" });
        var before = JsonSerializer.Serialize(new[] { layer, move, replace }); var result = Analyze(layer, move, replace).Before("g", null);
        Assert.That(result.Complete, Is.True); var target = result.Targets.Single();
        Assert.That(target.Resource, Is.EqualTo("other")); Assert.That(target.Values["transform.x"], Is.EqualTo(9));
        Assert.That(target.Values["flipbook.index"], Is.EqualTo(index));
        var capability = target.Properties.Single(property => property.Name == "flipbook.index");
        Assert.That(capability, Is.EqualTo(SceneLayer.AnimationProperties.Single(property => property.Name == "flipbook.index")));
        Assert.That(capability.Maximum, Is.Null, "FrameCount is a rendering clamp, not a runtime animation-value limit.");
        Assert.That(JsonSerializer.Serialize(new[] { layer, move, replace }), Is.EqualTo(before));
    }
    [TestCase("null", true, false)]
    [TestCase("{}", true, true)]
    [TestCase("{\"columns\":0}", false, false)]
    [TestCase("{\"columns\":2,\"rows\":2,\"frameCount\":5}", false, false)]
    [TestCase("{\"columns\":50000,\"rows\":50000}", false, false)]
    [TestCase("{\"index\":-1}", false, true)]
    public void EmptyInvalidOrUnkeyableFlipbooksNeverInventAValidInitialIndex(string json, bool complete, bool capability)
    {
        var layer = Layer("sheet", "sheet"); layer.Parameters["flipbook"] = JsonSerializer.Deserialize<JsonElement>(json);
        var result = Analyze(layer).Before("g", null); Assert.That(result.Complete, Is.EqualTo(complete));
        Assert.That(result.Targets.Single().Properties.Any(property => property.Name == "flipbook.index"), Is.EqualTo(capability));
        Assert.That(result.Targets.Single().Values.ContainsKey("flipbook.index"), Is.EqualTo(json == "{}"));
        Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Definite));
    }
    [TestCase("[]")]
    [TestCase("{\"index\":\"bad\"}")]
    public void MalformedFlipbookDoesNotPublishANewLayerOrEraseTheConservativeInput(string json)
    {
        var layer = Layer("sheet", "sheet"); layer.Parameters["flipbook"] = JsonSerializer.Deserialize<JsonElement>(json);
        var result = Analyze(Layer("old", "old"), layer).Before("g", null);
        Assert.That(result.Complete, Is.False); Assert.That(result.Targets.Single().Handle, Is.EqualTo("old"));
        Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Possible));
    }
    [Test]
    public void StaticReshowDropsFlipbookAndConditionsSeparateCapabilitiesFromUniqueValues()
    {
        var sheet = Entry("sheet", BuiltinEntryIds.ShowLayer, new { handleId = "sheet", flipbook = Sheet() }, "save.optional");
        var result = Analyze(sheet).Before("g", null); Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Possible));
        Assert.That(result.Targets.Single().Properties.Any(property => property.Name == "flipbook.index"), Is.True); Assert.That(result.Targets.Single().Values, Is.Empty);
        result = Analyze(sheet, Layer("reshow", "sheet")).Before("g", null);
        Assert.That(result.Targets.Single().Properties.Any(property => property.Name == "flipbook.index"), Is.False);
        Assert.That(result.Targets.Single().Values.ContainsKey("flipbook.index"), Is.False);
        var graph = Graph("root", "a", "b", "merge"); Edge(graph, "root", "a"); Edge(graph, "root", "b"); Edge(graph, "a", "merge"); Edge(graph, "b", "merge");
        sheet.Condition = "";
        var analysis = new SceneTargetAnalysis(graph, new Dictionary<string, List<EditorEntryData>> { ["root"] = [], ["a"] = [sheet], ["b"] = [Layer("static", "sheet")], ["merge"] = [] });
        result = analysis.Before("merge", null); Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Definite));
        Assert.That(result.Targets.Single().Properties.Any(property => property.Name == "flipbook.index"), Is.False);
    }
    [Test]
    public void EventCreatedFlipbooksKeepCapabilitiesForFutureCandidatesAndUseSharedIndexSampling()
    {
        var clip = Clip([Event(5, BuiltinEntryIds.ShowLayer, new { handleId = "sheet", flipbook = Sheet() }, "sheet-event")],
            [new { handleId = "sheet", property = "flipbook.index", keys = new[] { new { frame = 0, value = 1.5f }, new { frame = 20, value = 9.5f } } }]);
        var before = JsonSerializer.Serialize(clip); var analysis = Analyze(clip);
        var future = analysis.AtTimeline("g", "clip", new(0, IncludeFuture: true)).Targets.Single();
        Assert.That(future.IsFuture, Is.True); Assert.That(future.Values, Is.Empty); Assert.That(future.Properties.Any(property => property.Name == "flipbook.index"), Is.True);
        var current = analysis.AtTimeline("g", "clip", new(10)).Targets.Single();
        Assert.That(current.Values["flipbook.index"], Is.EqualTo(5.5f)); Assert.That(current.Locator, Is.EqualTo(future.Locator));
        Assert.That(analysis.Before("g", null).Targets.Single().Values["flipbook.index"], Is.EqualTo(9.5f));
        Assert.That(JsonSerializer.Serialize(clip), Is.EqualTo(before));
    }
}
