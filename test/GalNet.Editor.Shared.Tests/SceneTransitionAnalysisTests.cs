using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    private static readonly string[] TransitionTypes = [BuiltinEntryIds.CrossFadeTransition, BuiltinEntryIds.SlideTransition, BuiltinEntryIds.BlindsTransition,
        BuiltinEntryIds.BlackFadeTransition, BuiltinEntryIds.WhiteFadeTransition, BuiltinEntryIds.ColorFadeTransition];
    private static EditorEntryData Transition(string type, string condition = "")
    {
        var cross = type is BuiltinEntryIds.CrossFadeTransition or BuiltinEntryIds.BlindsTransition;
        Dictionary<string, object> parameters = new()
        {
            ["playbackHandleId"] = "transition-play", [cross ? "oldHandleId" : "fromLayerHandleId"] = "old",
            [cross ? "newHandleId" : "toLayerHandleId"] = "incoming", [cross ? "assetId" : "toAssetId"] = "new-image",
            [cross ? "transform" : "toTransform"] = new { x = 19, y = -3, scaleX = 2, scaleY = 3, rotationDegrees = 12 },
            ["future"] = new { number = 9007199254740993L, text = "old" }
        };
        if (type == BuiltinEntryIds.BlindsTransition) parameters["maskProgram"] = "mask-program";
        if (type == BuiltinEntryIds.ColorFadeTransition) parameters["color"] = "#336699";
        return Entry("transition", type, parameters, condition);
    }
    [TestCaseSource(nameof(TransitionTypes))]
    public void BlockingTransitionPublishesOnlyTerminalLayerWithAuthorLocatorAndKnownValues(string type)
    {
        var transition = Transition(type);
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Layer("old-layer", "old"), Layer("unrelated", "sibling", 8), transition], ["next"] = [] };
        var graph = Graph("g", "next"); Edge(graph, "g", "next");
        var before = JsonSerializer.Serialize(groups); var analysis = new SceneTargetAnalysis(graph, groups);
        Assert.That(analysis.Before("g", transition.StableId).Targets.Select(target => target.Handle), Is.EquivalentTo(new[] { "old", "sibling" }));
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var result = analysis.Before("next", null);
            Assert.That(result.Complete, Is.True, type);
            Assert.That(result.Targets.Select(target => target.Handle), Is.EquivalentTo(new[] { "incoming", "sibling" }));
            var incoming = result.Targets.Single(target => target.Handle == "incoming");
            Assert.That(incoming.Presence, Is.EqualTo(TargetPresence.Definite)); Assert.That(incoming.Kind, Is.EqualTo(SceneTargetKind.Layer));
            Assert.That(incoming.Resource, Is.EqualTo("new-image"));
            Assert.That(incoming.Locator, Is.EqualTo(new SceneLocator("g", transition.StableId, SceneAuthoringIdentities.CreationSlot(type)!)));
            Assert.That(incoming.CreatedAtFrame, Is.Null); Assert.That(incoming.IsFuture, Is.False);
            Assert.That(incoming.Values["transform.x"], Is.EqualTo(19)); Assert.That(incoming.Values["transform.y"], Is.EqualTo(-3));
            Assert.That(incoming.Values["transform.scaleX"], Is.EqualTo(2)); Assert.That(incoming.Values["transform.rotationDegrees"], Is.EqualTo(12));
            Assert.That(incoming.Values["opacity"], Is.EqualTo(1)); Assert.That(result.Targets.Single(target => target.Handle == "sibling").Values["transform.x"], Is.EqualTo(8));
        }
        Assert.That(JsonSerializer.Serialize(groups), Is.EqualTo(before), "Compilation input is a private projection, not author JSON.");
    }
    [TestCaseSource(nameof(TransitionTypes))]
    public void MissingOutgoingLayerDoesNotPreventIncomingCreationAndGeneratedHelpersDoNotLeak(string type)
    {
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Transition(type)] };
        var result = new SceneTargetAnalysis(Graph("g"), groups).Before("g", null);
        Assert.That(result.Complete, Is.True); Assert.That(result.Targets.Single().Handle, Is.EqualTo("incoming"));
        Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Definite));
    }
    [TestCaseSource(nameof(TransitionTypes))]
    public void ConditionalAndFalseTransitionsMergePresenceWithoutChangingUnrelatedObjects(string type)
    {
        var transition = Transition(type, "save.optional");
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Layer("old-layer", "old"), Layer("sibling", "sibling", 8), transition] };
        var result = new SceneTargetAnalysis(Graph("g"), groups).Before("g", null);
        Assert.That(result.Complete, Is.True); Assert.That(result.Targets.Where(target => target.Handle != "sibling").Select(target => target.Presence), Is.All.EqualTo(TargetPresence.Possible));
        Assert.That(result.Targets.Select(target => target.Handle), Is.EquivalentTo(new[] { "old", "incoming", "sibling" }));
        Assert.That(result.Targets.Where(target => target.Handle != "sibling").All(target => target.Values.Count == 0), Is.True);
        Assert.That(result.Targets.Single(target => target.Handle == "sibling").Values["transform.x"], Is.EqualTo(8));
        transition.Condition = "false";
        result = new SceneTargetAnalysis(Graph("g"), groups).Before("g", null);
        Assert.That(result.Targets.Select(target => target.Handle), Is.EquivalentTo(new[] { "old", "sibling" }));
        Assert.That(result.Targets.Select(target => target.Presence), Is.All.EqualTo(TargetPresence.Definite));
    }
    [TestCaseSource(nameof(TransitionTypes))]
    public void InvalidTransitionKeepsConservativeInputAndDoesNotPublishPartiallyExpandedObjects(string type)
    {
        var transition = Transition(type); transition.Parameters["playbackHandleId"] = JsonSerializer.SerializeToElement("");
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Layer("old-layer", "old"), transition] };
        var before = JsonSerializer.Serialize(groups); var analysis = new SceneTargetAnalysis(Graph("g"), groups);
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var result = analysis.Before("g", null); Assert.That(result.Complete, Is.False);
            Assert.That(result.Targets.Single().Handle, Is.EqualTo("old")); Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Possible));
            Assert.That(result.Targets.Single().Values, Is.Empty);
        }
        Assert.That(JsonSerializer.Serialize(groups), Is.EqualTo(before));
    }
    [Test]
    public void TransitionLoopEntryFactsConvergeAndInvalidationUsesANewSnapshot()
    {
        var graph = Graph("start", "loop"); Edge(graph, "start", "loop"); Edge(graph, "loop", "loop");
        var transition = Transition(BuiltinEntryIds.CrossFadeTransition);
        Dictionary<string, List<EditorEntryData>> groups = new() { ["start"] = [Layer("old-layer", "old")], ["loop"] = [transition] };
        var result = new SceneTargetAnalysis(graph, groups).Before("loop", transition.StableId);
        Assert.That(result.Complete, Is.True); Assert.That(result.Targets.Select(target => target.Handle), Is.EquivalentTo(new[] { "old", "incoming" }));
        Assert.That(result.Targets.Select(target => target.Presence), Is.All.EqualTo(TargetPresence.Possible));
        transition.Parameters["newHandleId"] = JsonSerializer.SerializeToElement("changed");
        result = new SceneTargetAnalysis(graph, groups).Before("loop", null);
        Assert.That(result.Targets.Single().Handle, Is.EqualTo("changed"));
    }
    [TestCaseSource(nameof(TransitionTypes))]
    public void MalformedTransitionTransformCannotApplyOnlyTheOutgoingDeletion(string type)
    {
        var transition = Transition(type);
        transition.Parameters[type is BuiltinEntryIds.CrossFadeTransition or BuiltinEntryIds.BlindsTransition ? "transform" : "toTransform"] = JsonSerializer.SerializeToElement(new { x = "invalid" });
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Layer("old-layer", "old"), transition] };
        var before = JsonSerializer.Serialize(groups); var analysis = new SceneTargetAnalysis(Graph("g"), groups);
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var result = analysis.Before("g", null);
            Assert.That(result.Complete, Is.False);
            Assert.That(result.Targets.Select(target => target.Handle), Is.EqualTo(new[] { "old" }));
            Assert.That(result.Targets.Single().Presence, Is.EqualTo(TargetPresence.Possible)); Assert.That(result.Targets.Single().Values, Is.Empty);
        }
        Assert.That(JsonSerializer.Serialize(groups), Is.EqualTo(before));
    }
    [Test]
    public void ValidTransitionDoesNotEraseEarlierAnalysisUncertainty()
    {
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [Layer("old-layer", "old"), Entry("unknown", "custom.unknown", new { }), Transition(BuiltinEntryIds.CrossFadeTransition)] };
        var result = new SceneTargetAnalysis(Graph("g"), groups).Before("g", null);
        Assert.That(result.Complete, Is.False);
        Assert.That(result.Targets.Single().Handle, Is.EqualTo("incoming")); Assert.That(result.Targets.Single().Values["opacity"], Is.EqualTo(1));
    }
}
