using System.Text.Json;
using GalNet.Core.Variants;

namespace GeneralTest.Variants;

public sealed class ImageVariantsTests
{
    private static ImageVariantsSource Source(string defaultKey = "idle_coat") => new()
    {
        Folder = "./alice",
        Default = defaultKey,
        Dimensions = [new() { Key = "expression", Label = "表情" }, new() { Key = "outfit", Label = "服装" }]
    };

    private static ImageVariantFile[] Files(params string[] keys) => keys.Select(key => new ImageVariantFile(key + ".png", "asset-" + key)).ToArray();

    private static ImageVariantsDefinition Build(ImageVariantsSource source, params string[] keys)
    {
        var result = ImageVariantsBuilder.Build(source, Files(keys));
        Assert.That(result.Diagnostics, Is.Empty);
        return result.Definition!;
    }

    private static ImageVariantsDefinition Example() => Build(Source(), "smile_coat", "smile_skirt", "idle_coat", "idle_skirt", "weap");

    [Test]
    public void Author_json_preserves_keys_and_labels_and_omits_inferred_dimensions()
    {
        var source = JsonSerializer.Deserialize<ImageVariantsSource>("""
            {"version":1,"folder":"./alice","default":"idle_coat","dimensions":[{"key":"expression","label":"表情"},{"key":"outfit","label":"服装"}]}
            """);
        Assert.That(JsonSerializer.Serialize(source), Does.Contain("\"default\":\"idle_coat\""));
        Assert.That(Build(source!, "idle_coat").Dimensions.Select(item => item.Key), Is.EqualTo(new[] { "expression", "outfit" }));
        Assert.That(JsonSerializer.Serialize(new ImageVariantsSource()), Does.Not.Contain("dimensions"));
    }

    [Test]
    public void Complete_selection_wins_and_default_initializes_only_its_present_dimensions()
    {
        var definition = Example();
        var initial = ImageVariantsResolver.Initialize(definition);
        var result = ImageVariantsResolver.Select(definition, initial.Request, "expression", "smile");
        Assert.Multiple(() =>
        {
            Assert.That(initial.Variant.Key, Is.EqualTo("idle_coat"));
            Assert.That(initial.ProtectedDimensions, Is.Empty);
            Assert.That(result.Variant.Key, Is.EqualTo("smile_coat"));
            Assert.That(result.Reason, Is.EqualTo(ImageVariantResolutionReason.Exact));
            Assert.That(result.Candidates, Has.Count.EqualTo(1));
            Assert.That(result.RemovedDimensions, Is.Empty);
        });
        var shortDefault = Build(Source("weap"), "idle_coat", "weap");
        Assert.That(ImageVariantsResolver.Initialize(shortDefault).Request.Keys, Is.EquivalentTo(new[] { "expression" }));
    }

    private static readonly string[] expected = new[] { "outfit" };

    [Test]
    public void Temporary_single_segment_expression_keeps_outfit_for_the_next_selection()
    {
        var definition = Example();
        var start = ImageVariantsResolver.Initialize(definition, new Dictionary<string, string> { ["expression"] = "smile", ["outfit"] = "skirt" });
        var special = ImageVariantsResolver.Select(definition, start.Request, "expression", "weap");
        var restored = ImageVariantsResolver.Select(definition, special.Request, "expression", "smile");
        Assert.Multiple(() =>
        {
            Assert.That(special.Variant.Key, Is.EqualTo("weap"));
            Assert.That(special.Reason, Is.EqualTo(ImageVariantResolutionReason.Reduced));
            Assert.That(special.Request["outfit"], Is.EqualTo("skirt"));
            Assert.That(special.RemovedDimensions, Is.EqualTo(expected));
            Assert.That(special.Candidates.Select(item => item.Selection.Count), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(special.Candidates[0].Match, Is.Null);
            Assert.That(special.Candidates[1].Match, Is.SameAs(special.Variant));
            Assert.That(restored.Variant.Key, Is.EqualTo("smile_skirt"));
            Assert.That(start.Variant.Key, Is.EqualTo("smile_skirt"));
        });
    }

    [Test]
    public void Missing_combination_does_not_substitute_another_expression_or_compact_positions()
    {
        // "skirt" is expression=skirt, never the outfit-only candidate {outfit:skirt}.
        var definition = Build(Source("smile_coat"), "smile_coat", "idle_skirt", "skirt");
        var result = ImageVariantsResolver.Select(definition, ImageVariantsResolver.Initialize(definition).Request, "outfit", "skirt");
        Assert.Multiple(() =>
        {
            Assert.That(result.Variant.Key, Is.EqualTo("smile_coat"));
            Assert.That(result.Reason, Is.EqualTo(ImageVariantResolutionReason.Default));
            Assert.That(result.Request["outfit"], Is.EqualTo("skirt"));
            Assert.That(result.ProtectedDimensions, Is.EqualTo(new[] { "outfit" }));
            Assert.That(result.RemovedDimensions, Is.EqualTo(new[] { "expression" }));
            Assert.That(result.Candidates[^1].Selection.Keys, Is.EquivalentTo(new[] { "outfit" }));
            Assert.That(result.Candidates.All(item => item.Match is null), Is.True);
        });
    }

    private static ImageVariantsDefinition ThreeDimensions()
    {
        var source = Source("smile_coat_day");
        source.Dimensions!.Add(new() { Key = "time" });
        return Build(source, "smile_coat_day", "idle_skirt_night", "idle", "smile_skirt");
    }

    [Test]
    public void Three_dimensions_drop_from_the_end_cumulatively()
    {
        var definition = ThreeDimensions();
        var request = new Dictionary<string, string> { ["expression"] = "idle", ["outfit"] = "coat", ["time"] = "night" };
        var result = ImageVariantsResolver.Resolve(definition, request, ["expression"]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Variant.Key, Is.EqualTo("idle"));
            Assert.That(result.RemovedDimensions, Is.EqualTo(new[] { "time", "outfit" }));
            Assert.That(result.Candidates.Select(item => item.Selection.Count), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(result.Candidates[1].Selection.Keys, Is.EquivalentTo(["expression", "outfit"]));
        });
    }

    [Test]
    public void Middle_dimension_is_protected_while_later_and_earlier_positions_are_removed()
    {
        var definition = ThreeDimensions();
        var request = new Dictionary<string, string> { ["expression"] = "idle", ["outfit"] = "coat", ["time"] = "night" };
        var result = ImageVariantsResolver.Select(definition, request, "outfit", "coat");
        Assert.Multiple(() =>
        {
            Assert.That(result.Reason, Is.EqualTo(ImageVariantResolutionReason.Default));
            Assert.That(result.RemovedDimensions, Is.EqualTo(new[] { "time", "expression" }));
            Assert.That(result.Candidates[^1].Selection.Keys, Is.EquivalentTo(new[] { "outfit" }));
        });
    }

    [Test]
    public void Multiple_initial_overrides_are_protected_in_dimension_order()
    {
        var definition = ThreeDimensions();
        var result = ImageVariantsResolver.Initialize(definition,
            new Dictionary<string, string> { ["outfit"] = "skirt", ["expression"] = "smile" });
        Assert.Multiple(() =>
        {
            Assert.That(result.Variant.Key, Is.EqualTo("smile_skirt"));
            Assert.That(result.RemovedDimensions, Is.EqualTo(new[] { "time" }));
            Assert.That(result.ProtectedDimensions, Is.EqualTo(new[] { "expression", "outfit" }));
            Assert.That(result.Request["time"], Is.EqualTo("day"));
        });
    }

    [Test]
    public void Selecting_the_same_value_again_changes_protection_and_can_change_fallback()
    {
        var definition = Example();
        var special = ImageVariantsResolver.Initialize(definition, new Dictionary<string, string> { ["expression"] = "weap" });
        var sameOutfit = ImageVariantsResolver.Select(definition, special.Request, "outfit", "coat");
        Assert.Multiple(() =>
        {
            Assert.That(special.Variant.Key, Is.EqualTo("weap"));
            Assert.That(sameOutfit.Request, Is.EquivalentTo(special.Request));
            Assert.That(sameOutfit.Reason, Is.EqualTo(ImageVariantResolutionReason.Default));
            Assert.That(sameOutfit.Variant.Key, Is.EqualTo("idle_coat"));
        });
    }

    [Test]
    public void Empty_request_and_empty_protection_are_deterministic()
    {
        var definition = Example();
        var empty = ImageVariantsResolver.Resolve(definition, new Dictionary<string, string>(), []);
        var unprotected = ImageVariantsResolver.Resolve(definition,
            new Dictionary<string, string> { ["expression"] = "weap", ["outfit"] = "skirt" }, []);
        Assert.Multiple(() =>
        {
            Assert.That(empty.Variant, Is.SameAs(definition.Default));
            Assert.That(empty.Reason, Is.EqualTo(ImageVariantResolutionReason.Default));
            Assert.That(empty.Candidates, Has.Count.EqualTo(1));
            Assert.That(unprotected.Variant.Key, Is.EqualTo("weap"));
        });
    }

    [TestCase("unknown", "smile")]
    [TestCase("expression", "SMILE")]
    [TestCase("expression", " smile")]
    [TestCase("outfit", "unknown")]
    public void Invalid_input_is_rejected_without_mutating_previous_request(string dimension, string value)
    {
        var definition = Example();
        var initial = ImageVariantsResolver.Initialize(definition);
        Assert.That(() => ImageVariantsResolver.Select(definition, initial.Request, dimension, value), Throws.ArgumentException);
        Assert.That(initial.Request["expression"], Is.EqualTo("idle"));
        Assert.That(initial.Request["outfit"], Is.EqualTo("coat"));
    }

    [Test]
    public void Protected_dimensions_must_be_present_in_validated_request()
    {
        var definition = Example();
        Assert.That(() => ImageVariantsResolver.Resolve(definition, new Dictionary<string, string>(), ["outfit"]), Throws.ArgumentException);
    }

    [Test]
    public void Missing_middle_position_does_not_match_a_prefix_with_the_same_value()
    {
        var definition = Build(Source("weap"), "weap", "coat", "idle_coat");
        var request = new Dictionary<string, string> { ["outfit"] = "coat" };
        var result = ImageVariantsResolver.Resolve(definition, request, ["outfit"]);
        Assert.That(result.Variant.Key, Is.EqualTo("weap"));
        Assert.That(result.Reason, Is.EqualTo(ImageVariantResolutionReason.Default));
    }

    [Test]
    public void Inference_and_ordering_are_independent_of_enumeration()
    {
        var source = new ImageVariantsSource { Folder = "./alice", Default = "idle_coat" };
        var files = Files("weap", "smile_skirt", "idle_coat");
        var first = ImageVariantsBuilder.Build(source, files).Definition!;
        var second = ImageVariantsBuilder.Build(source, files.Reverse()).Definition!;
        Assert.Multiple(() =>
        {
            Assert.That(first.Dimensions.Select(item => item.Key), Is.EqualTo(new[] { "d1", "d2" }));
            Assert.That(first.Dimensions.Select(item => item.Label), Is.EqualTo(new[] { "1", "2" }));
            Assert.That(first.Dimensions[0].Options, Is.EqualTo(new[] { "idle", "smile", "weap" }));
            Assert.That(second.Variants.Select(item => (item.Key, item.AssetId)), Is.EqualTo(first.Variants.Select(item => (item.Key, item.AssetId))));
            Assert.That(second.Dimensions.SelectMany(item => item.Options), Is.EqualTo(first.Dimensions.SelectMany(item => item.Options)));
        });
    }

    [Test]
    [SetCulture("ar-SA")]
    public void Dimension_count_is_not_fixed_and_generated_keys_use_invariant_numbers()
    {
        var values = Enumerable.Range(1, 16).Select(index => "v" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var key = string.Join('_', values);
        var source = new ImageVariantsSource { Folder = "./scene", Default = key };
        var definition = Build(source, key, "v1");
        var result = ImageVariantsResolver.Initialize(definition);
        Assert.Multiple(() =>
        {
            Assert.That(definition.Dimensions, Has.Count.EqualTo(16));
            Assert.That(definition.Dimensions[15].Key, Is.EqualTo("d16"));
            Assert.That(definition.Dimensions[15].Label, Is.EqualTo("16"));
            Assert.That(result.Request["d16"], Is.EqualTo("v16"));
            Assert.That(result.Variant.Key, Is.EqualTo(key));
        });
    }

    [Test]
    public void Explicit_extra_dimension_is_allowed_without_synthesizing_values()
    {
        var source = Source("idle");
        var definition = Build(source, "idle");
        Assert.That(definition.Dimensions[1].Options, Is.Empty);
        Assert.That(ImageVariantsResolver.Initialize(definition).Request.Keys, Is.EquivalentTo(new[] { "expression" }));
    }

    [TestCase("idle_.png", "empty-segment")]
    [TestCase("_idle.png", "empty-segment")]
    [TestCase("idle__coat.png", "empty-segment")]
    [TestCase(".png", "invalid-file-name")]
    [TestCase("idle", "invalid-file-name")]
    [TestCase("idle.", "invalid-file-name")]
    [TestCase("folder/idle.png", "invalid-file-name")]
    [TestCase("folder\\idle.png", "invalid-file-name")]
    public void Bad_file_names_produce_located_diagnostics_and_no_partial_definition(string fileName, string code)
    {
        var result = ImageVariantsBuilder.Build(Source("idle"), [new ImageVariantFile(fileName, "asset")]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Definition, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == code && item.Path == "/files/0/fileName"), Is.True);
        });
    }

    [TestCase("idle.png", "idle.webp")]
    [TestCase("idle.png", "Idle.png")]
    public void Duplicate_keys_across_extensions_or_case_are_rejected(string first, string second)
    {
        var result = ImageVariantsBuilder.Build(Source("idle"), [new(first, "a"), new(second, "b")]);
        Assert.That(result.Definition, Is.Null);
        Assert.That(result.Diagnostics.Any(item => item.Code == "duplicate-variant" && item.Path == "/files/1/fileName"), Is.True);
    }

    [Test]
    public void Builder_reports_bad_version_folder_default_identity_and_resource_type()
    {
        var source = Source("missing");
        source.Version = 99;
        source.Folder = " ";
        var result = ImageVariantsBuilder.Build(source, [new("idle.png", "", "audio")]);
        Assert.That(result.Diagnostics.Select(item => item.Code), Is.EquivalentTo(new[]
        {
            "unsupported-version", "missing-folder", "invalid-default", "missing-asset-id", "invalid-resource-type"
        }));
        Assert.That(result.Definition, Is.Null);
        Assert.That(ImageVariantsBuilder.Build(Source(), []).Diagnostics.Any(item => item.Code == "empty-variants"), Is.True);
    }

    [Test]
    public void Builder_rejects_duplicate_empty_null_or_insufficient_dimensions()
    {
        var source = Source();
        source.Dimensions = [new() { Key = "same" }, new() { Key = "same" }, new() { Key = " " }, null!];
        var result = ImageVariantsBuilder.Build(source, Files("idle_coat"));
        Assert.That(result.Definition, Is.Null);
        Assert.That(result.Diagnostics.Select(item => item.Path), Is.EquivalentTo(new[] { "/dimensions/1/key", "/dimensions/2/key", "/dimensions/3/key" }));
        source.Dimensions = [];
        Assert.That(ImageVariantsBuilder.Build(source, Files("idle_coat")).Diagnostics.Any(item => item.Code == "insufficient-dimensions"), Is.True);
    }

    [Test]
    public void Keys_values_and_default_are_exact_while_labels_are_only_display_metadata()
    {
        var source = Source("Idle_Coat");
        source.Dimensions![0].Label = "renamed";
        var definition = Build(source, "Idle_Coat");
        Assert.That(definition.Dimensions[0].Options, Is.EqualTo(new[] { "Idle" }));
        source.Default = "idle_coat";
        Assert.That(ImageVariantsBuilder.Build(source, Files("Idle_Coat")).Diagnostics.Any(item => item.Code == "invalid-default"), Is.True);
        Assert.That(definition.Dimensions[0].Key, Is.EqualTo("expression"));
    }

    [Test]
    public void Definitions_requests_candidates_and_instances_do_not_share_mutable_inputs()
    {
        var source = Source();
        var files = Files("idle_coat", "smile_skirt", "weap");
        var definition = ImageVariantsBuilder.Build(source, files).Definition!;
        source.Dimensions![0].Key = "changed";
        source.Dimensions[0].Label = "changed";
        files[0] = new("other.png", "other");
        var selection = new Dictionary<string, string> { ["expression"] = "weap", ["outfit"] = "skirt" };
        var protectedKeys = new List<string> { "expression" };
        var first = ImageVariantsResolver.Resolve(definition, selection, protectedKeys);
        var second = ImageVariantsResolver.Initialize(definition);
        selection["expression"] = "idle";
        protectedKeys.Clear();
        Assert.Multiple(() =>
        {
            Assert.That(definition.Dimensions[0].Key, Is.EqualTo("expression"));
            Assert.That(definition.Dimensions[0].Label, Is.EqualTo("表情"));
            Assert.That(definition.Default.AssetId, Is.EqualTo("asset-idle_coat"));
            Assert.That(first.Request["expression"], Is.EqualTo("weap"));
            Assert.That(first.Candidates[0].Selection["expression"], Is.EqualTo("weap"));
            Assert.That(first.ProtectedDimensions, Is.EqualTo(new[] { "expression" }));
            Assert.That(second.Request["expression"], Is.EqualTo("idle"));
            Assert.That(() => ((IList<string>)definition.Dimensions[0].Options)[0] = "mutated", Throws.TypeOf<NotSupportedException>());
            Assert.That(() => ((IList<string>)definition.Default.Values)[0] = "mutated", Throws.TypeOf<NotSupportedException>());
        });
    }
}
