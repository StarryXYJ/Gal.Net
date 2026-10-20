using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    [TestCase(BuiltinEntryIds.CrossFadeTransition, "oldHandleId", "newHandleId")]
    [TestCase(BuiltinEntryIds.BlindsTransition, "oldHandleId", "newHandleId")]
    [TestCase(BuiltinEntryIds.SlideTransition, "fromLayerHandleId", "toLayerHandleId")]
    [TestCase(BuiltinEntryIds.BlackFadeTransition, "fromLayerHandleId", "toLayerHandleId")]
    [TestCase(BuiltinEntryIds.WhiteFadeTransition, "fromLayerHandleId", "toLayerHandleId")]
    [TestCase(BuiltinEntryIds.ColorFadeTransition, "fromLayerHandleId", "toLayerHandleId")]
    public void TransitionCreatesFreshOutputAndPlaybackAndCopyRewritesOnlyInternalReferences(string type, string inputSlot, string outputSlot)
    {
        var original = Entry("transition", type, new { playbackHandleId = "play", assetId = "incoming", future = new { value = "incoming", number = 9007199254740993L } });
        original.Parameters[inputSlot] = JsonSerializer.SerializeToElement("outside");
        original.Parameters[outputSlot] = JsonSerializer.SerializeToElement("incoming");
        var before = JsonSerializer.Serialize(original);
        var created = SceneAuthoringIdentities.NewEntry(type, new(original.Parameters));
        var another = SceneAuthoringIdentities.NewEntry(type, new(original.Parameters));
        Assert.That(created[outputSlot].GetString(), Is.Not.EqualTo("incoming").And.Not.EqualTo(another[outputSlot].GetString()));
        Assert.That(created["playbackHandleId"].GetString(), Is.Not.EqualTo("play").And.Not.EqualTo(created[outputSlot].GetString()));
        Assert.That(created[inputSlot].GetString(), Is.EqualTo("outside"));
        Assert.That(JsonSerializer.Serialize(original), Is.EqualTo(before));

        var stop = Entry("stop", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "play" });
        var hide = Entry("hide", BuiltinEntryIds.HideLayer, new { handleId = "incoming" });
        var effect = Entry("effect", BuiltinEntryIds.ApplyEffect, new { instanceId = "effect", targetHandleId = "incoming" });
        var timeline = Entry("timeline", BuiltinEntryIds.PlayTimeline, new { timeline = new { playbackHandleId = "timeline", tracks = new[] { new { handleId = "incoming" } } } });
        // References can appear before their creator; discovery must finish before rewriting.
        SceneAuthoringIdentities.RemapCopy([stop, hide, effect, timeline, original]);
        var output = original.Parameters[outputSlot].GetString();
        Assert.That(output, Is.Not.EqualTo("incoming"));
        Assert.That(original.Parameters[inputSlot].GetString(), Is.EqualTo("outside"));
        Assert.That(hide.Parameters["handleId"].GetString(), Is.EqualTo(output));
        Assert.That(effect.Parameters["targetHandleId"].GetString(), Is.EqualTo(output));
        Assert.That(timeline.Parameters["timeline"].GetProperty("tracks")[0].GetProperty("handleId").GetString(), Is.EqualTo(output));
        Assert.That(stop.Parameters["playbackHandleId"].GetString(), Is.EqualTo(original.Parameters["playbackHandleId"].GetString()).And.Not.EqualTo("play"));
        Assert.That(original.Parameters["assetId"].GetString(), Is.EqualTo("incoming"));
        Assert.That(original.Parameters["future"].GetProperty("value").GetString(), Is.EqualTo("incoming"));
        Assert.That(original.Parameters["future"].GetProperty("number").GetInt64(), Is.EqualTo(9007199254740993L));

        var source = Layer("source", "outside");
        var secondTransition = Entry("second", type, new { playbackHandleId = "second-play" });
        secondTransition.Parameters[inputSlot] = JsonSerializer.SerializeToElement("outside");
        secondTransition.Parameters[outputSlot] = JsonSerializer.SerializeToElement("second-output");
        SceneAuthoringIdentities.RemapCopy([secondTransition, source]);
        Assert.That(secondTransition.Parameters[inputSlot].GetString(), Is.EqualTo(source.Parameters["handleId"].GetString()).And.Not.EqualTo("outside"));
    }
}
