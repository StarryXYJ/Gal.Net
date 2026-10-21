using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    private static EditorEntryData Playback(string id = "animate", string handle = "play", string loop = "Loop", bool blocking = false, float duration = 1, string condition = "") =>
        Entry(id, BuiltinEntryIds.Animate, new { playbackHandleId = handle, handleId = "layer", property = "opacity", loopMode = loop, blocking = blocking ? "true" : "false", duration }, condition);
    [TestCase("Once", false, 1, 1)]
    [TestCase("Once", true, 1, 0)]
    [TestCase("Once", false, 0, 0)]
    [TestCase("Loop", false, 1, 1)]
    [TestCase("PingPong", false, 1, 1)]
    [TestCase("Loop", true, 1, 1)]
    public void PlaybackCandidatesAreSeparateConservativeAndNeverInventAnIdentity(string loop, bool blocking, float duration, int count)
    {
        var entry = Playback(loop: loop, blocking: blocking, duration: duration); var original = JsonSerializer.Serialize(entry);
        var result = Analyze(Layer("layer", "layer"), entry).Before("g", null);
        Assert.That(result.Complete, Is.True);
        Assert.That(result.Targets.Single().Kind, Is.EqualTo(SceneTargetKind.Layer)); Assert.That(result.Playbacks, Has.Count.EqualTo(count));
        foreach (var target in result.Playbacks)
        {
            Assert.That(target.Locator, Is.EqualTo(new SceneLocator("g", "animate", "playbackHandleId")));
            Assert.That(target.Presence, Is.EqualTo(TargetPresence.Possible)); Assert.That(target.Properties, Is.Empty); Assert.That(target.Values, Is.Empty);
        }
        Assert.That(JsonSerializer.Serialize(entry), Is.EqualTo(original));
        entry.Parameters.Remove("playbackHandleId"); Assert.That(Analyze(entry).Before("g", null).Playbacks, Is.Empty);
    }
    [TestCase("AfterIteration", "", 1)]
    [TestCase("CompleteImmediately", "", 0)]
    [TestCase("CompleteImmediately", "save.optional", 1)]
    [TestCase("CompleteImmediately", "false", 1)]
    public void StopPlaybackModesPreservePendingStopAndConditionUncertainty(string mode, string condition, int count)
    {
        var stop = Entry("stop", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "play", mode }, condition);
        var result = Analyze(Layer("layer", "layer"), Playback(), stop).Before("g", null);
        Assert.That(result.Playbacks, Has.Count.EqualTo(count)); Assert.That(result.Targets.Single().Handle, Is.EqualTo("layer"));
        Assert.That(result.Targets.Single().Values, Is.Empty, "Stopping a playback does not invent its displayed terminal value.");
        Assert.That(result.Complete, Is.True);
        Assert.That(Analyze(Entry("missing", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "absent", mode })).Before("g", null).Playbacks, Is.Empty);
    }
    [TestCase(true, "Once", 20, 0)]
    [TestCase(false, "Once", 20, 1)]
    [TestCase(false, "Once", 0, 0)]
    [TestCase(false, "Loop", 20, 1)]
    [TestCase(false, "PingPong", 20, 1)]
    public void TimelinePlaybackUsesNestedStableLocatorAndCurrentFramePresence(bool blocking, string loopMode, int duration, int count)
    {
        var clip = Entry("clip", BuiltinEntryIds.PlayTimeline, new { timeline = new { PlaybackHandleId = "timeline-play", durationFrames = duration, loopMode, blocking, tracks = new object[0], events = new object[0] } });
        var analysis = Analyze(clip); var original = JsonSerializer.Serialize(clip);
        Assert.That(analysis.Before("g", null).Playbacks, Has.Count.EqualTo(count));
        var active = analysis.AtTimeline("g", "clip", new(0)).Playbacks.Single();
        Assert.That(active.Locator, Is.EqualTo(new SceneLocator("g", "clip", "timeline.playbackHandleId")));
        Assert.That(active.Presence, Is.EqualTo(duration == 0 ? TargetPresence.Possible : TargetPresence.Definite));
        Assert.That(analysis.AtTimeline("g", "clip", new(duration)).Playbacks.Single().Presence, Is.EqualTo(TargetPresence.Possible));
        Assert.That(JsonSerializer.Serialize(clip), Is.EqualTo(original));
    }
    [Test]
    public void PlaybackBranchesCyclesUnknownEntriesAndIdentityConflictsRemainConservative()
    {
        var graph = Graph("root", "a", "b", "merge"); Edge(graph, "root", "a"); Edge(graph, "root", "b"); Edge(graph, "a", "merge"); Edge(graph, "b", "merge");
        Dictionary<string, List<EditorEntryData>> groups = new() { ["root"] = [], ["a"] = [Playback("left")], ["b"] = [], ["merge"] = [] };
        var analysis = new SceneTargetAnalysis(graph, groups); Assert.That(analysis.Before("merge", null).Playbacks.Single().Presence, Is.EqualTo(TargetPresence.Possible));
        groups["b"] = [Playback("right")]; analysis = new(graph, groups);
        Assert.That(analysis.Before("merge", null).Playbacks.Single().Kind, Is.EqualTo(SceneTargetKind.Unresolved), "Two creators cannot share one resolved locator.");
        var invalid = Analyze(Layer("collision", "play"), Playback()).Before("g", null);
        Assert.That(invalid.Targets.Single().Kind, Is.EqualTo(SceneTargetKind.Layer)); Assert.That(invalid.Playbacks.Single().Kind, Is.EqualTo(SceneTargetKind.Unresolved)); Assert.That(invalid.Complete, Is.False);
        var unknown = Analyze(Playback(), Entry("custom", "custom.unknown", new { })).Before("g", null);
        Assert.That(unknown.Complete, Is.False); Assert.That(unknown.Playbacks.Single().Presence, Is.EqualTo(TargetPresence.Possible));
        var cycle = Graph("g"); Edge(cycle, "g", "g"); groups = new() { ["g"] = [Playback()] };
        Assert.That(new SceneTargetAnalysis(cycle, groups).Before("g", null).Playbacks.Single().Presence, Is.EqualTo(TargetPresence.Possible));
        Assert.That(new SceneTargetAnalysis(cycle, groups, budget: 0).Before("g", null).Reachable, Is.False, "No processed entry point cannot be presented as an analyzed empty scene.");
        Assert.That(new SceneTargetAnalysis(cycle, groups, budget: 1).Before("g", null).Complete, Is.False);
        Assert.That(new SceneTargetAnalysis(cycle, groups, budget: 1).Before("g", null).Playbacks.Single().Presence, Is.EqualTo(TargetPresence.Possible));
    }
    [TestCaseSource(nameof(TransitionTypes))]
    public void CompletedTransitionsNeverLeakTheirInternalPlayback(string type)
    {
        var result = Analyze(Layer("old", "old"), Playback(), Transition(type)).Before("g", null);
        Assert.That(result.Playbacks.Single().Locator.EntryId, Is.EqualTo("animate"));
    }
    [Test]
    public void PlaybackCopyRemapsBothSourcesAndStopsWithoutTouchingUnrelatedStrings()
    {
        var animate = Playback(); var clip = Entry("clip", BuiltinEntryIds.PlayTimeline, new { timeline = new { playbackHandleId = "timeline-play", durationFrames = 20, events = new object[0], tracks = new object[0] } });
        var first = Entry("first-stop", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "play", future = "play" });
        var second = Entry("second-stop", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "timeline-play" });
        SceneAuthoringIdentities.RemapCopy([first, second, animate, clip]);
        Assert.That(first.Parameters["playbackHandleId"].GetString(), Is.EqualTo(animate.Parameters["playbackHandleId"].GetString()).And.Not.EqualTo("play"));
        Assert.That(second.Parameters["playbackHandleId"].GetString(), Is.EqualTo(clip.Parameters["timeline"].GetProperty("playbackHandleId").GetString()).And.Not.EqualTo("timeline-play"));
        Assert.That(first.Parameters["future"].GetString(), Is.EqualTo("play"));
        Assert.That(Analyze(animate, clip).Before("g", null).Playbacks.Select(target => target.Handle), Is.EquivalentTo(new[] { first.Parameters["playbackHandleId"].GetString(), second.Parameters["playbackHandleId"].GetString() }));
    }
    [TestCase("{\"duration\":\"bad\"}")]
    [TestCase("{\"loopMode\":\"invalid\"}")]
    [TestCase("{\"blocking\":false}")]
    public void MalformedPlaybackNeverPublishesANewCandidate(string raw)
    {
        var entry = Playback(); foreach (var item in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw)!) entry.Parameters[item.Key] = item.Value;
        var result = Analyze(entry).Before("g", null); Assert.That(result.Complete, Is.False); Assert.That(result.Playbacks, Is.Empty);
    }
}
