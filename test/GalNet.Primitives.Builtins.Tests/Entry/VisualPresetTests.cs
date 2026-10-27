using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;

namespace GeneralTest.Entry;

public sealed class VisualPresetTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    internal static TimelineDefinition Compile(string type, object parameters)
    {
        var source = new GroupDocument
        {
            Entries = [new() { Id = "preset", Type = type, Condition = "true",
            Parameters = JsonSerializer.SerializeToElement(parameters).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) }]
        };
        var compiled = GalgroupCompiler.Compile(source, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).Document.Entries.Single();
        Assert.That(compiled.TypeId, Is.EqualTo(BuiltinEntryIds.PlayTimeline));
        return compiled.Arguments.GetProperty("timeline").Deserialize<TimelineDefinition>(Options)!;
    }

    [TestCase("Wipe")]
    [TestCase("Radial")]
    [TestCase("Noise")]
    [TestCase("Blinds")]
    [TestCase("Mask")]
    public void Reveal_passes_explicit_program_and_cleans_helpers_after_reveal(string mode)
    {
        var timeline = Compile(BuiltinEntryIds.RevealTransition, new { playbackHandleId = "reveal", oldHandleId = "old", newHandleId = "new", assetId = "image", program = "chosen-shader", mode, maskAsset = "mask-image" });
        var apply = timeline.Events.Single(e => e.Type == BuiltinEntryIds.ApplyEffect);
        Assert.That(apply.Parameters["program"].GetString(), Is.EqualTo("chosen-shader"));
        Assert.That(apply.Parameters["targetHandleId"].GetString(), Is.EqualTo("new"));
        if (mode == "Mask") Assert.That(apply.Parameters["parameters"].GetProperty("mask").GetString(), Is.EqualTo("mask-image"));
        Assert.That(timeline.Tracks.Single().Keys.Last().Value, Is.EqualTo(1));
        Assert.That(timeline.Events.Count(e => e.Type == BuiltinEntryIds.StopEffect && e.Frame == timeline.DurationFrames), Is.EqualTo(1));
        Assert.That(timeline.Events.Last().Parameters["handleId"].GetString(), Is.EqualTo("old"));
    }

    [Test]
    public void Blur_transition_has_two_passes_on_each_layer_and_zero_terminal_incoming_blur()
    {
        var timeline = Compile(BuiltinEntryIds.BlurTransition, new { playbackHandleId = "blur", oldHandleId = "old", newHandleId = "new", assetId = "image", blurProgram = "chosen-blur" });
        var effects = timeline.Events.Where(e => e.Type == BuiltinEntryIds.ApplyEffect).ToArray();
        Assert.That(effects, Has.Length.EqualTo(4));
        Assert.That(effects.All(e => e.Parameters["program"].GetString() == "chosen-blur"), Is.True);
        var incoming = effects.Where(e => e.Parameters["targetHandleId"].GetString() == "new").Select(e => e.Parameters["instanceId"].GetString()).ToArray();
        Assert.That(timeline.Tracks.Where(t => incoming.Contains(t.HandleId)).All(t => t.Keys.Last().Value == 0), Is.True);
        Assert.That(timeline.Events.Count(e => e.Type == BuiltinEntryIds.StopEffect), Is.EqualTo(4));
    }

    [TestCase(MotionPreset.FadeIn)]
    [TestCase(MotionPreset.FadeOut)]
    [TestCase(MotionPreset.SlideIn)]
    [TestCase(MotionPreset.SlideOut)]
    [TestCase(MotionPreset.PopIn)]
    [TestCase(MotionPreset.Jump)]
    [TestCase(MotionPreset.Nod)]
    [TestCase(MotionPreset.ShakeHead)]
    [TestCase(MotionPreset.Push)]
    public void Motion_supports_shortest_clip_and_retains_valid_strict_key_order(MotionPreset motion)
    {
        var timeline = Compile(BuiltinEntryIds.MotionPreset, new { playbackHandleId = "motion", handleId = "hero", motion = motion.ToString(), durationFrames = 2 });
        TimelineEventCapabilities.ValidateStructure(timeline);
        Assert.That(timeline.Tracks, Is.Not.Empty);
        if (motion is MotionPreset.Jump or MotionPreset.Nod or MotionPreset.ShakeHead)
            Assert.That(timeline.Tracks.All(t => t.BlendMode == AnimationBlendMode.Additive && t.Keys.Last().Value == 0), Is.True);
    }

    [Test]
    public void Push_scales_about_authored_anchor_and_adds_directional_translation()
    {
        var timeline = Compile(BuiltinEntryIds.MotionPreset, new { playbackHandleId = "push", handleId = "hero", motion = "Push", transform = new { x = 100, y = 40, scaleX = 2, scaleY = 2 }, anchorX = 200, anchorY = 40, scale = 1.5, distance = 20, angle = 0 });
        Assert.That(timeline.Tracks.Single(t => t.Property == "transform.x").Keys.Last().Value, Is.EqualTo(70));
        Assert.That(timeline.Tracks.Single(t => t.Property == "transform.scaleX").Keys.Last().Value, Is.EqualTo(3));
    }

    [TestCase(2)]
    [TestCase(36)]
    public void Separate_shake_entries_move_one_target_each_and_return_to_zero(int frames)
    {
        var background = Compile(BuiltinEntryIds.ShakePreset, new { playbackHandleId = "quake-background", handleId = "background", durationFrames = frames });
        var hero = Compile(BuiltinEntryIds.ShakePreset, new { playbackHandleId = "quake-hero", handleId = "hero", durationFrames = frames });
        var backgroundX = background.Tracks.Single(t => t.Property == "transform.x");
        var heroX = hero.Tracks.Single(t => t.Property == "transform.x");
        Assert.That(backgroundX.Keys.Select(k => k.Value), Is.EqualTo(heroX.Keys.Select(k => k.Value)));
        Assert.That(backgroundX.Keys.Any(k => Math.Abs(k.Value) > 1), Is.True);
        Assert.That(background.Tracks.All(t => t.HandleId == "background" && t.BlendMode == AnimationBlendMode.Additive && t.Keys.Last().Value == 0), Is.True);
        Assert.That(hero.Tracks.All(t => t.HandleId == "hero" && t.BlendMode == AnimationBlendMode.Additive && t.Keys.Last().Value == 0), Is.True);
    }

    [TestCase(BuiltinEntryIds.SpeedLinesPreset)]
    [TestCase(BuiltinEntryIds.ImpactPreset)]
    public void Dynamic_lines_compile_time_tracks_and_terminal_cleanup(string type)
    {
        var parameters = new Dictionary<string, object> { ["playbackHandleId"] = "impact", ["program"] = "lines-shader", ["blocking"] = false, ["durationFrames"] = 60 };
        if (type == BuiltinEntryIds.ImpactPreset) parameters["handleId"] = "hero";
        var timeline = Compile(type, parameters);
        var time = timeline.Tracks.Single(t => t.Property == "time");
        Assert.That(time.Keys.Last().Value, Is.EqualTo(1));
        Assert.That(timeline.Blocking, Is.False);
        var created = timeline.Events.Where(e => e.Type == BuiltinEntryIds.ShowColorLayer).Select(e => e.Parameters["handleId"].GetString()).ToArray();
        var removed = timeline.Events.Where(e => e.Type == BuiltinEntryIds.HideLayer).Select(e => e.Parameters["handleId"].GetString()).ToArray();
        Assert.That(removed, Is.EquivalentTo(created));
        Assert.That(timeline.Events.Single(e => e.Type == BuiltinEntryIds.StopEffect).Frame, Is.EqualTo(timeline.DurationFrames));
    }

    [Test]
    public void Sustained_shake_keeps_amplitude_and_ignores_blocking_and_click_skip()
    {
        var timeline = Compile(BuiltinEntryIds.ShakePreset, new
        {
            playbackHandleId = "quake",
            handleId = "hero",
            durationFrames = 60,
            cycles = 4,
            amplitude = 16,
            playbackMode = "UntilStopped",
            blocking = true,
            skippable = true
        });
        Assert.Multiple(() =>
        {
            Assert.That(timeline.LoopMode, Is.EqualTo(AnimationLoopMode.Loop));
            Assert.That(timeline.Blocking, Is.False);
            Assert.That(timeline.Skippable, Is.False);
            var x = timeline.Tracks.Single(track => track.Property == "transform.x");
            Assert.That(x.Keys[1].Value, Is.EqualTo(x.Keys[5].Value).Within(.001));
            Assert.That(x.Keys[^1].Value, Is.Zero);
        });
        Assert.Throws<InvalidDataException>(() => Compile(BuiltinEntryIds.ShakePreset,
            new { playbackHandleId = "quake", handleId = "hero", playbackMode = "forever" }));
    }

    [Test]
    public void Sustained_lines_keep_opacity_and_use_playback_setup_and_completion_events()
    {
        var timeline = Compile(BuiltinEntryIds.SpeedLinesPreset, new { playbackHandleId = "lines", program = "shader", playbackMode = "UntilStopped" });
        Assert.That(timeline.Tracks.Any(track => track.Property == "opacity"), Is.False);
        Assert.That(timeline.Events.Where(item => item.Frame == 0).All(item => item.Execution == TimelineEventExecution.Once), Is.True);
        Assert.That(timeline.Events.Where(item => item.Frame > 0).All(item => item.Execution == TimelineEventExecution.OnCompletion), Is.True);
        var invalid = timeline.Events.First();
        invalid.Execution = (TimelineEventExecution)42;
        Assert.Throws<InvalidDataException>(() => TimelineEventCapabilities.ValidateStructure(timeline));
        invalid.Execution = TimelineEventExecution.OnCompletion;
        Assert.Throws<InvalidDataException>(() => TimelineEventCapabilities.ValidateStructure(timeline));
    }

    [Test]
    public void Missing_shader_target_and_reserved_handle_collisions_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => Compile(BuiltinEntryIds.SpeedLinesPreset, new { playbackHandleId = "p" }));
        Assert.Throws<InvalidDataException>(() => Compile(BuiltinEntryIds.ShakePreset, new { playbackHandleId = "p", handleId = "" }));
        Assert.Throws<InvalidDataException>(() => Compile(BuiltinEntryIds.ShakePreset, new { playbackHandleId = "p", targets = new[] { new { handleId = "hero" } } }));
        Assert.Throws<InvalidDataException>(() => Compile(BuiltinEntryIds.ImpactPreset, new { playbackHandleId = "p", program = "shader", handleId = "p/visual/lines" }));
        Assert.Throws<InvalidDataException>(() => Compile(BuiltinEntryIds.RevealTransition, new { playbackHandleId = "p", oldHandleId = "same", newHandleId = "same", assetId = "image", program = "shader" }));
    }
}
