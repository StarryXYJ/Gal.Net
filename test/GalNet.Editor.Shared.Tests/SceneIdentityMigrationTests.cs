using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    [Test]
    public void CompatibleRolesPreserveOutputAcrossDifferentWireSlotsWhileIncompatibleKindsAllocateFreshIdentity()
    {
        var layer = Layer("creator", "layer-identity");
        var transition = SceneAuthoringIdentities.ChangeType(layer.Type, layer.Parameters, BuiltinEntryIds.SlideTransition, new());
        Assert.That(transition["toLayerHandleId"].GetString(), Is.EqualTo("layer-identity"));
        var playback = transition["playbackHandleId"].GetString(); Assert.That(playback, Is.Not.Empty);
        var cross = SceneAuthoringIdentities.ChangeType(BuiltinEntryIds.SlideTransition, transition, BuiltinEntryIds.CrossFadeTransition, new());
        Assert.That(cross["newHandleId"].GetString(), Is.EqualTo("layer-identity")); Assert.That(cross["playbackHandleId"].GetString(), Is.EqualTo(playback));
        var effect = SceneAuthoringIdentities.ChangeType(BuiltinEntryIds.CrossFadeTransition, cross, BuiltinEntryIds.ApplyEffect, new());
        Assert.That(effect["instanceId"].GetString(), Is.Not.EqualTo("layer-identity").And.Not.EqualTo(playback));
        Assert.That(layer.Parameters["handleId"].GetString(), Is.EqualTo("layer-identity"));
        var timeline = SceneAuthoringIdentities.ChangeType(BuiltinEntryIds.CrossFadeTransition, cross, BuiltinEntryIds.PlayTimeline,
            new() { ["timeline"] = JsonSerializer.SerializeToElement(new { PlaybackHandleId = "template", unknown = new { number = 9007199254740993L } }) });
        Assert.That(timeline["timeline"].GetProperty("PlaybackHandleId").GetString(), Is.EqualTo(playback));
        Assert.That(timeline["timeline"].GetProperty("unknown").GetProperty("number").GetInt64(), Is.EqualTo(9007199254740993L));
        var animate = SceneAuthoringIdentities.ChangeType(BuiltinEntryIds.PlayTimeline, timeline, BuiltinEntryIds.Animate, new());
        Assert.That(animate["playbackHandleId"].GetString(), Is.EqualTo(playback));
    }
}
