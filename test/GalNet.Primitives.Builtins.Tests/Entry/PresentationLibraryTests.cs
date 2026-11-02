using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;

namespace GeneralTest.Entry;

public sealed class PresentationLibraryTests
{
    [Test]
    public void Parallax_array_elements_expose_the_same_wire_names_used_by_author_json()
    {
        using var resolver = new GalNet.Core.Parameters.ValueStructureResolver();
        var array = resolver.Resolve(ParallaxPresetOptions.Schema.Single(field => field.Id == "targets"));
        var element = resolver.Resolve(array.Element!);
        Assert.That(element.Members.Select(member => member.WireName), Is.EquivalentTo(new[] { "handleId", "transform", "factor" }));
    }

    [TestCase("Blink")] [TestCase("Open")] [TestCase("Close")] [TestCase("LetterboxIn")] [TestCase("LetterboxOut")]
    public void Masks_select_scope_and_clean_up_only_transient_or_opened_overlays(string motion)
    {
        var timeline = VisualPresetTests.Compile("preset.mask", new { playbackHandleId = "blink", program = "mask", instanceId = "eyes", motion, overlay = true, durationFrames = 60 });
        Assert.That(timeline.Events.First().Parameters["overlay"].GetBoolean(), Is.True);
        Assert.That(timeline.Tracks.Single().Keys.Last().Value, Is.EqualTo(motion is "Close" or "LetterboxOut" ? 0 : 1));
        Assert.That(timeline.Events.Any(e => e.Type == "layer.hide"), Is.EqualTo(motion is "Blink" or "Open" or "LetterboxOut"));
    }

    [TestCase("Rain")] [TestCase("Snow")] [TestCase("Petals")] [TestCase("Dust")] [TestCase("Fireflies")]
    public void Weather_owns_one_emitter_until_stopped_and_has_a_valid_particle_definition(string weather)
    {
        var timeline = VisualPresetTests.Compile("preset.weather", new { playbackHandleId = "weather", texture = "texture", weather });
        Assert.That(timeline.Blocking, Is.False); Assert.That(timeline.LoopMode, Is.EqualTo(AnimationLoopMode.Loop));
        Assert.That(timeline.Events.First().Execution, Is.EqualTo(TimelineEventExecution.Once));
        Assert.That(timeline.Events.Last().Execution, Is.EqualTo(TimelineEventExecution.OnCompletion));
        var definition = ParticleEmitterDefinition.FromJson(timeline.Events.First().Parameters["parameters"].GetRawText());
        Assert.That(definition.InitialModule.Lifetime.Min, Is.GreaterThan(0));
    }

    [TestCase("transition.flash")] [TestCase("transition.zoom")] [TestCase("transition.pixelate")] [TestCase("transition.glitch")]
    public void Transitions_end_with_original_incoming_transform_and_remove_outgoing_and_temporary_effects(string type)
    {
        var timeline = VisualPresetTests.Compile(type, new { playbackHandleId = "cut", oldHandleId = "old", newHandleId = "new", assetId = "image", program = "shader", transform = new { x = 15, y = 20, scaleX = 1.2, scaleY = .8 } });
        Assert.That(timeline.Events.Last().Parameters["handleId"].GetString(), Is.EqualTo("old"));
        foreach (var apply in timeline.Events.Where(e => e.Type == "effect.apply"))
            Assert.That(timeline.Events.Any(e => e.Type == "effect.stop" && e.Parameters["instanceId"].GetString() == apply.Parameters["instanceId"].GetString()), Is.True);
    }

    [Test]
    public void Explicit_emphasis_composes_existing_text_and_voice_without_reading_runtime_scene()
    {
        var source = new GroupDocument { Entries = [new() { Id = "emphasis", Type = "dialogue.emphasis", Parameters = JsonSerializer.SerializeToElement(new {
            playbackHandleId = "focus", program = "grade", instanceId = "focus-style", handleIds = new[] { "alice", "bob" }, activeHandleId = "alice", content = "line-key", voice = "voice", trackId = "vocal"
        }).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) }] };
        var entries = GalgroupCompiler.Compile(source, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).Document.Entries;
        Assert.That(entries.Select(e => e.TypeId), Is.EqualTo(new[] { "timeline.play", "audio.play", "dialogue.text" }));
        Assert.That(entries[0].Arguments.GetProperty("timeline").GetProperty("events")[0].GetProperty("parameters").GetProperty("ensureOnly").GetBoolean(), Is.True);
        Assert.That(entries[0].Arguments.GetProperty("timeline").GetProperty("tracks")[0].GetProperty("startFromCurrent").GetBoolean(), Is.True);
        Assert.That(entries.Last().Arguments.GetProperty("content").GetString(), Is.EqualTo("line-key"));
    }

    [Test]
    public void Audio_tracks_validate_order_and_unique_target_and_allow_persistent_effect_events()
    {
        var timeline = new TimelineDefinition { FrameRate = 60, DurationFrames = 60, AudioTracks = [new() { TargetId = "bgm", Keys = [new() { Frame = 0, Value = 1 }, new() { Frame = 60, Value = .4f }] }] };
        Assert.DoesNotThrow(() => TimelineEventCapabilities.ValidateStructure(timeline));
        timeline.AudioTracks.Add(timeline.AudioTracks[0]);
        Assert.Throws<InvalidDataException>(() => TimelineEventCapabilities.ValidateStructure(timeline));
        Assert.That(TimelineEventCapabilities.Supports("audio.play"), Is.True);
        Assert.That(TimelineEventCapabilities.Supports("audio.seffect.update"), Is.True);
    }
}
