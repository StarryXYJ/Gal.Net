using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class SceneTargetAnalysisTests
{
    [Test]
    public void KnownTimelineTargetDoesNotInheritUnrelatedEffectsIncompleteCapabilities()
    {
        var descriptor = new ShaderEffectDescriptor(new("blur"), 1, "source", new HashSet<EffectStage> { EffectStage.ScenePost },
            [new("radius", "radius", ShaderEffectParameterKind.Float, "0", 0, 64, Animatable: true)]);
        var effect = Entry("fx", BuiltinEntryIds.ApplyEffect, new { instanceId = "c2121d4d69544d4d8a584d392dfedbe6", program = "blur", parameters = new { radius = 32 } });
        var unrelated = Entry("unknown", BuiltinEntryIds.ApplyEffect, new { instanceId = "unrelated", program = "missing" });
        var clip = Entry("clip", BuiltinEntryIds.PlayTimeline, new
        {
            timeline = new
            {
                playbackHandleId = "play",
                frameRate = 60,
                durationFrames = 60,
                tracks = new[] { new { handleId = "c2121d4d69544d4d8a584d392dfedbe6", property = "radius", blendMode = "Replace",
                keys = new[] { new { frame = 0, value = 32f }, new { frame = 10, value = 34.3f } } } },
                events = new[] { new { frame = 8, type = BuiltinEntryIds.ShowLayer, editorId = "create", parameters = new { transform = new { }, z = 0, opacity = 1, displayMode = "Native", handleId = "new-layer" } } }
            }
        });
        Dictionary<string, List<EditorEntryData>> groups = new() { ["g"] = [effect, unrelated, clip] };
        var analysis = new SceneTargetAnalysis(Graph("g"), groups, programs: new Dictionary<string, ShaderEffectDescriptor?> { ["blur"] = descriptor, ["missing"] = null });
        var before = analysis.AtTimeline("g", "clip", new(0));
        Assert.That(before.Complete, Is.False, "The overall query still reports the unrelated metadata gap.");
        Assert.That(SceneReferenceAnalysis.Diagnose(new(groups), analysis).Where(issue => issue.JsonPointer == "/parameters/timeline/tracks/0/handleId"), Is.Empty);

        // The same target's missing shader capabilities must still warn.
        analysis = new(Graph("g"), groups, programs: new Dictionary<string, ShaderEffectDescriptor?> { ["blur"] = null });
        Assert.That(SceneReferenceAnalysis.Diagnose(new(groups), analysis).Single(issue => issue.JsonPointer == "/parameters/timeline/tracks/0/handleId").Code,
            Is.EqualTo("scene.reference.uncertain"));
        groups["g"] = [unrelated, clip];
        Assert.That(SceneReferenceAnalysis.Diagnose(new(groups), new(Graph("g"), groups)).Single(issue => issue.JsonPointer == "/parameters/timeline/tracks/0/handleId").Code,
            Is.EqualTo("scene.reference.missing"));
    }
}
