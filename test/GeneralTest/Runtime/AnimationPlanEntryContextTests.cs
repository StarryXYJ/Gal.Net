using GalNet.Core.Entry;
using GalNet.Runtime.Handlers;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class AnimationPlanEntryContextTests
{
    [Test]
    public void Plan_deserializes_nested_tracks_keys_and_events()
    {
        var plan = ReadPlan("""
            { "playbackHandleId": "scene-enter-clip", "frameRate": 30, "durationFrames": 12, "blocking": true, "batchId": "scene-enter",
              "tracks": [ { "handleId": "hero", "property": "opacity", "keys": [
                { "frame": 0, "value": 0, "outTangent": 0.5, "interpolationToNext": "CubicHermite" },
                { "frame": 12, "value": 1, "inTangent": 0.25 }
              ] } ],
              "events": [ { "frame": 0, "type": "layer.show", "parameters": { "handleId": "hero", "assetId": "hero.png" } } ] }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(plan.FrameRate, Is.EqualTo(30));
            Assert.That(plan.DurationFrames, Is.EqualTo(12));
            Assert.That(plan.Tracks.Single().Keys[0].OutTangent, Is.EqualTo(.5f));
            Assert.That(plan.Events.Single().Parameters["assetId"].GetString(), Is.EqualTo("hero.png"));
        });
    }

    [TestCase("""{ "durationFrames": 2, "tracks": [ { "handleId": "hero", "property": "opacity", "keys": [ { "frame": 1, "value": 0 } ] } ] }""")]
    [TestCase("""{ "durationFrames": 2, "tracks": [ { "handleId": "hero", "property": "opacity", "keys": [ { "frame": 0, "value": 0 }, { "frame": 0, "value": 1 } ] } ] }""")]
    [TestCase("""{ "durationFrames": 2, "tracks": [ { "handleId": "hero", "property": "opacity", "keys": [ { "frame": 0, "value": 0 }, { "frame": 3, "value": 1 } ] } ] }""")]
    public void Plan_rejects_invalid_key_frames(string json)
    {
        Assert.That(() => ReadPlan(json), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Loop_plan_requires_nonblocking_unskippable_playback()
    {
        Assert.That(() => ReadPlan("""
            { "playbackHandleId": "loop", "loopMode": "Loop", "durationFrames": 30, "blocking": true,
              "tracks": [ { "handleId": "hero", "property": "opacity", "keys": [ { "frame": 0, "value": 0 } ] } ] }
            """), Throws.TypeOf<InvalidDataException>());
    }

    private static GalNet.Core.Scene.AnimationPlanDefinition ReadPlan(string json)
    {
        var entry = EntryRegistry.Create(PlayAnimationPlanEntry.TypeId, values: new Dictionary<string, string> { ["plan"] = json });
        return new EntryContext { Entry = entry, Runtime = new GameRuntime(null) }.GetAnimationPlan();
    }
}
