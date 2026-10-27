using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shared.Visuals;

namespace GalNet.Editor.Shared.Tests;

public sealed class VisualPresetAuthoringTests
{
    private static Dictionary<string, JsonElement> Parameters(object value) => JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    [Test]
    public void Default_fill_is_a_saved_explicit_reference_and_does_not_replace_author_selection()
    {
        var configuration = new DefaultProjectOptions { DefaultVisualPrograms = new() { ["speedLines"] = "initial", ["maskReveal"] = "mask-program" } }.ToConfiguration();
        var parameters = VisualPresetBindings.Fill(BuiltinEntryIds.SpeedLinesPreset, new(), configuration);
        Assert.That(parameters["program"].GetString(), Is.EqualTo("initial"));
        configuration.Set(DefaultProjectConfiguration.Section, new DefaultProjectOptions { DefaultVisualPrograms = new() { ["speedLines"] = "changed" } });
        Assert.That(parameters["program"].GetString(), Is.EqualTo("initial"));
        Assert.That(VisualPresetBindings.Fill(BuiltinEntryIds.SpeedLinesPreset, parameters, configuration)["program"].GetString(), Is.EqualTo("initial"));
        Assert.That(VisualPresetBindings.Fill(BuiltinEntryIds.BlurTransition, new(), configuration), Is.Empty);
    }

    [Test]
    public void Nested_shake_targets_are_indexed_and_remapped_with_copied_layers()
    {
        var layer = new EditorEntryData { StableId = "layer", Type = BuiltinEntryIds.ShowColorLayer, Parameters = Parameters(new { handleId = "hero", color = "#FFFFFF" }) };
        var shake = new EditorEntryData { StableId = "shake", Type = BuiltinEntryIds.ShakePreset, Parameters = Parameters(new { playbackHandleId = "quake", targets = new[] { new { handleId = "hero" } } }) };
        var index = new SceneReferenceIndex(new Dictionary<string, List<EditorEntryData>> { ["g"] = [layer, shake] });
        Assert.That(index.Complete, Is.True);
        Assert.That(index.References.Single().JsonPointer, Is.EqualTo("/parameters/targets/0/handleId"));
        SceneAuthoringIdentities.RemapCopy([layer, shake]);
        Assert.That(shake.Parameters["targets"][0].GetProperty("handleId").GetString(), Is.EqualTo(layer.Parameters["handleId"].GetString()));
        Assert.That(layer.Parameters["handleId"].GetString(), Is.Not.EqualTo("hero"));
    }

    [Test]
    public void Generated_visual_helpers_do_not_leak_into_terminal_scene_analysis()
    {
        var graph = new EditorGraphDocument { RootNodeId = "g", Nodes = [new() { Id = "g", Type = "Group" }] };
        var layer = new EditorEntryData { StableId = "hero", Type = BuiltinEntryIds.ShowColorLayer, Parameters = Parameters(new { handleId = "hero", color = "#FFFFFF" }) };
        var impact = new EditorEntryData { StableId = "impact", Type = BuiltinEntryIds.ImpactPreset, Parameters = Parameters(new { playbackHandleId = "impact-play", program = "shader", targets = new[] { new { handleId = "hero" } } }) };
        var analysis = new SceneTargetAnalysis(graph, new Dictionary<string, List<EditorEntryData>> { ["g"] = [layer, impact] });
        var result = analysis.Before("g", null);
        Assert.That(result.Complete, Is.True);
        Assert.That(result.Targets.Select(target => target.Handle), Is.EqualTo(new[] { "hero" }));
    }

    [Test]
    public void Sustained_presets_are_selectable_by_their_authored_playback_slot()
    {
        var graph = new EditorGraphDocument { RootNodeId = "g", Nodes = [new() { Id = "g", Type = "Group" }] };
        var layer = new EditorEntryData { StableId = "hero", Type = BuiltinEntryIds.ShowColorLayer, Parameters = Parameters(new { handleId = "hero", color = "#FFFFFF" }) };
        var shake = new EditorEntryData
        {
            StableId = "shake",
            Type = BuiltinEntryIds.ShakePreset,
            Parameters = Parameters(new
            { playbackHandleId = "quake", targets = new[] { new { handleId = "hero" } }, playbackMode = "UntilStopped" })
        };
        var stop = new EditorEntryData { StableId = "stop", Type = BuiltinEntryIds.StopAnimation, Parameters = Parameters(new { playbackHandleId = "quake", mode = "CompleteImmediately" }) };
        var analysis = new SceneTargetAnalysis(graph, new Dictionary<string, List<EditorEntryData>> { ["g"] = [layer, shake, stop] });
        var candidate = analysis.Before("g", "stop").Playbacks.Single();
        Assert.That(candidate.Handle, Is.EqualTo("quake"));
        Assert.That(candidate.Locator.Slot, Is.EqualTo("playbackHandleId"));
        Assert.That(candidate.Locator.EntryId, Is.EqualTo("shake"));
        Assert.That(candidate.Resource, Is.EqualTo(BuiltinEntryIds.ShakePreset));
        Assert.That(analysis.Before("g", null).Playbacks, Is.Empty);
    }

    [Test]
    public void Project_validation_checks_shader_contract_and_missing_resource_without_hidden_fallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "GalNet-preset-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var plan = DefaultVisualTemplate.Prepare(new DefaultProjectOptions().ToConfiguration());
            foreach (var (file, bytes) in plan.Files)
            {
                var path = Path.Combine(root, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
            }
            var program = plan.Configuration.ReadDefaultOptions().DefaultVisualPrograms["speedLines"];
            var document = new GroupDocument { Entries = [new() { Id = "lines", Type = BuiltinEntryIds.SpeedLinesPreset, Parameters = Parameters(new { playbackHandleId = "lines-play", program }) }] };
            var groupFile = Path.Combine(root, "sample.rawgalgroup");
            File.WriteAllText(groupFile, JsonSerializer.Serialize(document));
            var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
            var diagnostics = new List<ToolDiagnostic>();
            Assert.That(new ProjectValidationService(root).ValidateGroup("sample.rawgalgroup", diagnostics, catalog), Is.Not.Null, JsonSerializer.Serialize(diagnostics));
            var compiled = GalgroupCompiler.Compile(document, catalog).Document.Entries.Single();
            Assert.That(compiled.Arguments.GetRawText(), Does.Contain(program));
            File.WriteAllText(Path.Combine(root, "Assets/Effects/Default/SpeedLines.sksl"), DefaultVisualTemplate.ReadShader("Vignette"));
            diagnostics.Clear();
            Assert.That(new ProjectValidationService(root).ValidateGroup("sample.rawgalgroup", diagnostics, catalog), Is.Null);
            Assert.That(diagnostics.Single().Code, Is.EqualTo("visual.programInvalid"));
            Assert.That(diagnostics.Single().Message, Does.Contain("time"));
            File.Delete(Path.Combine(root, "Assets/Effects/Default/SpeedLines.sksl"));
            diagnostics.Clear();
            Assert.That(new ProjectValidationService(root).ValidateGroup("sample.rawgalgroup", diagnostics, catalog), Is.Null);
            Assert.That(diagnostics.Single().Message, Does.Contain("missing"));
        }
        finally { Directory.Delete(root, true); }
    }
}
