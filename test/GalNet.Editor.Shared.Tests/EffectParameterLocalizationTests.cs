using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class EffectParameterLocalizationTests
{
    private static EditorEntryData Entry(string id, string type, object parameters) => new()
    { StableId = id, Type = type, Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(parameters))! };
    private static EditorProjectDocument Document(params EditorEntryData[] entries) => new()
    {
        Graph = new() { RootNodeId = "g", Nodes = [new() { Id = "g", Type = "Group" }] },
        GroupEntries = new() { ["g"] = entries.ToList() }
    };
    private static LocalizationReferenceIndex Scan(EditorProjectDocument document, EntryParameterMetadata? metadata = null) =>
        new(document, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()), [], null, metadata ?? new());

    [Test]
    public void AllKnownAudioEffectsUseTheirCatalogFieldsWithoutMutatingAuthorData()
    {
        var document = Document(SEffectCatalog.Builtins.Definitions.Select(effect => Entry(effect.Id, BuiltinEntryIds.ApplyAudioSEffect,
            new { id = effect.Id, parameters = effect.DefaultParameters })).ToArray());
        var before = JsonSerializer.Serialize(document);
        var index = Scan(document);
        Assert.That(index.Complete, Is.True);
        Assert.That(index.Issues, Is.Empty);
        Assert.That(index.References, Is.Empty);
        Assert.That(JsonSerializer.Serialize(document), Is.EqualTo(before));
        var entry = document.GroupEntries["g"][0];
        entry.Parameters["parameters"] = JsonSerializer.Deserialize<JsonElement>("""{"unknown":{"caption":"unclassified"}}""");
        var issue = Scan(document).Issues.Single();
        Assert.That(issue.Code, Is.EqualTo("field.unknown"));
        Assert.That(issue.JsonPointer, Is.EqualTo("/parameters/unknown"));
        Assert.That(issue.EntryId, Is.EqualTo(entry.StableId));
        entry.Parameters["id"] = JsonSerializer.SerializeToElement("custom.unavailable");
        Assert.That(Scan(document).Issues.Single().Code, Is.EqualTo("field.opaque"));
    }

    [TestCase(BuiltinEntryIds.ApplyEffect)]
    [TestCase(BuiltinEntryIds.SpeedLinesPreset)]
    [TestCase(BuiltinEntryIds.ImpactPreset)]
    public void ShaderParametersShareFormFieldsIncludingVectorsAndOrdinaryStrings(string type)
    {
        var shader = new ShaderEffectDescriptor(new("shader"), 1, "source", new HashSet<EffectStage> { EffectStage.Layer, EffectStage.ScenePost },
        [new("amount", "amount", ShaderEffectParameterKind.Float, Minimum: 0, Maximum: 1),
            new("direction", "direction", ShaderEffectParameterKind.Vector3), new("tint", "tint", ShaderEffectParameterKind.Color),
            new("mask", "mask", ShaderEffectParameterKind.Texture), new("style", "style", ShaderEffectParameterKind.Enum, Options: ["soft", "hard"]),
            new("count", "count", ShaderEffectParameterKind.Integer), new("enabled", "enabled", ShaderEffectParameterKind.Boolean),
            new("offset", "offset", ShaderEffectParameterKind.Vector2), new("channels", "channels", ShaderEffectParameterKind.Vector4)]);
        var metadata = new EntryParameterMetadata(Programs: new Dictionary<string, ShaderEffectDescriptor?> { ["shader"] = shader });
        var entry = Entry("fx", type, new
        {
            program = "shader",
            parameters = new
            {
                amount = .5,
                direction = new[] { 1, 2, 3 },
                tint = "#11223344",
                mask = "image",
                style = "soft",
                count = 2,
                enabled = true,
                offset = new[] { 0, 1 },
                channels = new[] { 1, 2, 3, 4 }
            }
        });
        var document = Document(entry);
        Assert.That(Scan(document, metadata).Issues, Is.Empty);
        Assert.That(Scan(document, metadata).References, Is.Empty, "Colors, texture IDs and enum choices are not localized text keys.");
        entry.Parameters["parameters"] = JsonSerializer.Deserialize<JsonElement>("""{"amount":2,"direction":[1,"bad",3],"unknown":42}""");
        var invalid = Scan(document, metadata);
        Assert.That(invalid.Issues.Select(issue => (issue.Code, issue.JsonPointer)), Is.EquivalentTo(new[]
        { ("value.range", "/parameters/amount"), ("value.type", "/parameters/direction/1") }));
        entry.Parameters["program"] = JsonSerializer.SerializeToElement("missing");
        Assert.That(Scan(document, metadata).Issues.Single().Code, Is.EqualTo("field.opaque"));
    }

    [TestCase(BuiltinEntryIds.ApplyEffect)]
    [TestCase(BuiltinEntryIds.SpeedLinesPreset)]
    [TestCase(BuiltinEntryIds.ImpactPreset)]
    public void KnownShaderAcceptsPreservedAdditionalParametersAsLegalUninterpretedData(string type)
    {
        var parsed = ShaderEffectMetadataParser.Parse(new("bloom"), GalNet.Editor.Shared.Visuals.DefaultVisualTemplate.ReadShader("Bloom"));
        var entry = Entry("fx", type, new
        {
            program = "bloom",
            parameters = new
            {
                angle = 0,
                strength = 4,
                softKnee = .2,
                radius = 32,
                threshold = .5,
                future = new { caption = "literal", nested = new[] { "not-a-localization-key" } }
            }
        });
        var document = Document(entry); var before = JsonSerializer.Serialize(document);
        var metadata = new EntryParameterMetadata(Programs: new Dictionary<string, ShaderEffectDescriptor?> { ["bloom"] = parsed.Descriptor });
        var index = Scan(document, metadata);
        Assert.That(index.Issues, Is.Empty); Assert.That(index.Complete, Is.True); Assert.That(index.References, Is.Empty);
        Assert.That(JsonSerializer.Serialize(document), Is.EqualTo(before));
        entry.Parameters["parameters"] = JsonSerializer.Deserialize<JsonElement>("""{"radius":32,"angle":0,"angle":1}""");
        Assert.That(Scan(document, metadata).Issues.Single().Code, Is.EqualTo("field.duplicate"));
        Assert.That(Scan(document).Issues.Single().Code, Is.EqualTo("field.opaque"), "The open policy requires a known shader declaration.");
    }

    [Test]
    public void RegisteredVisualAndCustomAudioCatalogsSupplyTheSameWireFieldsAsValidation()
    {
        var effects = new EffectCatalog([new("custom.fx", EffectStage.Layer, [new("strength", EffectParameterKind.Float), new("label", EffectParameterKind.Text)], [])]);
        var audio = new SEffectCatalog([new("custom.audio", new HashSet<SEffectScope> { SEffectScope.Bus }, [new("wet", Minimum: 0, Maximum: 1)])]);
        var document = Document(Entry("fx", BuiltinEntryIds.ApplyEffect, new { id = "custom.fx", parameters = new { strength = .2, label = "literal" } }),
            Entry("audio", BuiltinEntryIds.ApplyAudioSEffect, new { id = "custom.audio", parameters = new { wet = .4 } }));
        var metadata = new EntryParameterMetadata(effects, audio);
        Assert.That(Scan(document, metadata).Issues, Is.Empty);
        document.GroupEntries["g"][0].Parameters["program"] = JsonSerializer.SerializeToElement("missing-program");
        Assert.That(Scan(document, metadata).Issues.Single().Code, Is.EqualTo("field.opaque"), "An explicit program must not fall back to a different ID catalog.");
    }

    [Test]
    public void AudioUpdateResolvesThePriorInstanceByScopeTargetAndIdentityAndNeverUsesFutureAttachments()
    {
        var track = Entry("track", BuiltinEntryIds.ApplyAudioSEffect, new { scope = "track", targetId = "bgm", instanceId = "same", id = "seffect.reverb", parameters = new { mix = .25 } });
        var bus = Entry("bus", BuiltinEntryIds.ApplyAudioSEffect, new { scope = "bus", targetId = "bgm", instanceId = "same", id = "seffect.gain", parameters = new { gain = .5 } });
        var update = Entry("update", BuiltinEntryIds.UpdateAudioSEffect, new { scope = "track", targetId = "bgm", instanceId = "same", parameters = new { mix = .4 } });
        var stop = Entry("stop", BuiltinEntryIds.StopAudioSEffect, new { scope = "track", targetId = "bgm", instanceId = "same" });
        var document = Document(track, bus, update, stop);
        Assert.That(Scan(document).Issues, Is.Empty);
        document.GroupEntries["g"] = [update, track, bus, stop];
        Assert.That(Scan(document).Issues.Single().Code, Is.EqualTo("field.opaque"));
        document.GroupEntries["g"] = [track, bus, stop, update];
        Assert.That(Scan(document).Issues.Single().Code, Is.EqualTo("field.opaque"));
        document.GroupEntries["g"] = [track, bus, update, stop];
        update.Parameters["scope"] = JsonSerializer.SerializeToElement("bus");
        Assert.That(Scan(document).Issues.Single().JsonPointer, Is.EqualTo("/parameters/mix"));
    }

    [Test]
    public async Task DiskValidationReadsShaderMetadataAndRetainsMissingOrAmbiguousResourceWarnings()
    {
        var root = Path.Combine(Path.GetTempPath(), "GalNet-effect-localization-" + Guid.NewGuid().ToString("N"));
        try
        {
            await EditorProjectCreator.CreateAsync(new(root, "Effects"));
            var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
            var repository = new EditorDocumentRepository(catalog, EditorDocumentValidation.Authoring);
            var loaded = repository.Load(root);
            var group = loaded.GroupEntries.Keys.Single();
            var id = Guid.NewGuid().ToString("N");
            loaded.GroupEntries[group] = [Entry("fx", BuiltinEntryIds.ApplyEffect, new { program = id, parameters = new { amount = .5 } }),
                Entry("audio", BuiltinEntryIds.ApplyAudioSEffect, new { id = "seffect.reverb", parameters = new { mix = .4 } })];
            var plan = repository.PrepareSave(root, loaded.Document, loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
            repository.ApplySave(root, plan);
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            var shader = Path.Combine(root, "Assets", "test.sksl");
            await File.WriteAllTextAsync(shader, """
                /*
                @gal.effect v=1
                @input source
                @targets scenePost
                @param amount
                  uniform: amount
                  type: float
                */
                """);
            await File.WriteAllTextAsync(shader + ".meta", JsonSerializer.Serialize(new { id, type = "effect-program" }));
            Assert.That(LocalizationValidation.ValidateDisk(root, catalog), Is.Empty);
            await File.WriteAllTextAsync(shader + ".duplicate.meta", JsonSerializer.Serialize(new { id, type = "effect-program" }));
            Assert.That(LocalizationValidation.ValidateDisk(root, catalog).Any(issue => issue.Code == "localization.field.opaque" && issue.ObjectId == "fx"), Is.True);
            File.Delete(shader + ".duplicate.meta");
            File.Delete(shader);
            Assert.That(LocalizationValidation.ValidateDisk(root, catalog).Any(issue => issue.Code == "localization.field.opaque" && issue.ObjectId == "fx"), Is.True);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
