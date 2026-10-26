using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Variants;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public sealed class ImageVariantsRuntimeTests
{
    private static ImageVariantsCatalog Catalog(string specialAsset = "sprite-weap")
    {
        var definition = ImageVariantsBuilder.Build(new()
        {
            Folder = ".",
            Default = "idle_coat",
            Dimensions = [new() { Key = "expression" }, new() { Key = "outfit" }]
        }, new[] { "idle_coat", "idle_skirt", "smile_coat", "smile_skirt", "weap" }
            .Select(key => new ImageVariantFile(key + ".png", key == "weap" ? specialAsset : "sprite-" + key))).Definition!;
        return new([new("atlas", definition)]);
    }

    private static CompositeGameView View(Presenter presenter, ImageVariantsCatalog? catalog = null) =>
        new GameRegistration().RegisterSchema(BuiltinSchemas.CreateLayersModule()).AddBuiltinLayers(presenter, catalog ?? Catalog()).BuildView();

    private static PrimitiveInstance Execute(CompositeGameView view, GameRuntime runtime, string type, object options,
        PrimitiveInvocationMode mode = PrimitiveInvocationMode.Normal, CancellationToken ct = default) =>
        view.Dispatch(new PrimitiveEntry(type, JsonSerializer.SerializeToElement(options), "batch"), runtime, ct, mode);

    private static object Show(string handle = "hero") => new
    {
        handleId = handle,
        assetId = "atlas",
        selection = new { expression = "smile", outfit = "skirt" },
        transform = new { x = 15, y = 20, scaleX = 1.5 },
        z = 12,
        opacity = .4f,
        displayMode = "Native"
    };

    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public async Task Public_instructions_keep_request_and_restore_special_expression_in_all_modes(PrimitiveInvocationMode mode)
    {
        var presenter = new Presenter();
        using var view = View(presenter);
        view.RequireExecutionCoverage();
        var runtime = new GameRuntime(null, "root", stateCatalog: view.StateCatalog);
        var instance = Execute(view, runtime, BuiltinEntryIds.ShowVariants, Show(), mode);
        Execute(view, runtime, BuiltinEntryIds.SelectVariants, new { handleId = "hero", dimension = "expression", value = "weap" }, mode);
        var layer = runtime.State.Table<Layer>().Get("hero");
        Assert.Multiple(() =>
        {
            Assert.That(instance.IsCompleted, Is.True);
            Assert.That(instance.IsBlocking, Is.False);
            Assert.That(instance.BatchId, Is.EqualTo("batch"));
            Assert.That(layer.AssetId, Is.EqualTo("sprite-weap"));
            Assert.That(layer.Variants!.Request["outfit"], Is.EqualTo("skirt"));
            Assert.That(layer.Variants.Reason, Is.EqualTo(ImageVariantResolutionReason.Reduced));
            Assert.That(presenter.Shown, Has.Count.EqualTo(mode == PrimitiveInvocationMode.CatchUp ? 0 : 1));
            Assert.That(presenter.Replaced, Has.Count.EqualTo(mode == PrimitiveInvocationMode.CatchUp ? 0 : 1));
        });
        var snapshot = runtime.CreateSnapshot();
        Assert.That(snapshot.States["builtin.layers"].Version, Is.EqualTo(2));
        runtime.RestoreFrom(snapshot);
        await using var restorer = new StateRestoreSession(view);
        await restorer.RestoreAsync(runtime);
        Assert.That(presenter.Shown[^1].AssetId, Is.EqualTo("sprite-weap"));
        Execute(view, runtime, BuiltinEntryIds.SelectVariants, new { handleId = "hero", dimension = "expression", value = "smile" });
        Assert.That(runtime.State.Table<Layer>().Get("hero").AssetId, Is.EqualTo("sprite-smile_skirt"));
    }

    [Test]
    public void Selection_keeps_instance_transform_and_animation_values_while_other_instances_remain_independent()
    {
        var presenter = new Presenter();
        using var view = View(presenter);
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        Execute(view, runtime, BuiltinEntryIds.ShowVariants, Show());
        Execute(view, runtime, BuiltinEntryIds.ShowVariants, Show("second"));
        var original = runtime.State.Table<Layer>().Get("hero");
        original.TrySetAnimationValue("opacity", .6f, out _);
        Execute(view, runtime, BuiltinEntryIds.SelectVariants, new { handleId = "hero", dimension = "expression", value = "weap" });
        Assert.Multiple(() =>
        {
            Assert.That(runtime.State.Table<Layer>().Get("hero"), Is.SameAs(original));
            Assert.That(original.Transform.X, Is.EqualTo(15));
            Assert.That(original.Transform.Y, Is.EqualTo(20));
            Assert.That(original.Transform.ScaleX, Is.EqualTo(1.5f));
            Assert.That(original.Opacity, Is.EqualTo(.6f));
            Assert.That(original.Z, Is.EqualTo(12));
            Assert.That(runtime.State.Table<Layer>().Get("second").AssetId, Is.EqualTo("sprite-smile_skirt"));
        });
        Assert.That(original.TrySetAnimationValue("opacity", .7f, out _), Is.True);
        Assert.That(runtime.State.Table<Layer>().Get("hero").Opacity, Is.EqualTo(.7f));
    }

    [Test]
    public async Task Selecting_a_difference_does_not_stop_an_active_loop_animation()
    {
        var animations = new PendingAnimations();
        using var view = new GameRegistration().AddBuiltins(layerPresenter: new Presenter(), animationPresenter: animations, imageVariants: Catalog()).BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        Execute(view, runtime, BuiltinEntryIds.ShowVariants, Show());
        var instance = new AnimationPrimitiveInstance(runtime, animations, new AnimationRequest
        {
            PlaybackHandleId = "loop",
            HandleId = "hero",
            Property = "opacity",
            From = .2f,
            To = .8f,
            DurationSeconds = 1,
            LoopMode = AnimationLoopMode.Loop
        }, null, default);
        instance.Dispatch();
        var loop = runtime.State.Table<LoopAnimationState>().Get("loop");
        Execute(view, runtime, BuiltinEntryIds.SelectVariants, new { handleId = "hero", dimension = "expression", value = "weap" });
        Assert.That(instance.IsCompleted, Is.False);
        Assert.That(runtime.State.Table<LoopAnimationState>().Get("loop"), Is.SameAs(loop));
        Assert.That(runtime.Scene().TryGet<AnimationPlaybackInstance>("loop", out _), Is.True);
        animations.Completion.SetResult(AnimationOutcome.Completed);
        await instance.PresentationCompletion;
    }

    [TestCase("layer.replace")]
    [TestCase("layer.show")]
    [TestCase("layer.showColor")]
    [TestCase("layer.hide")]
    public void Replacing_or_hiding_exits_variant_mode(string type)
    {
        using var view = View(new());
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        Execute(view, runtime, BuiltinEntryIds.ShowVariants, Show());
        object options = type switch
        {
            "layer.hide" => new { handleId = "hero" },
            "layer.showColor" => new { handleId = "hero", color = "#112233" },
            _ => new { handleId = "hero", assetId = "plain" }
        };
        Execute(view, runtime, type, options);
        Assert.Throws<InvalidOperationException>(() => Execute(view, runtime, BuiltinEntryIds.SelectVariants,
            new { handleId = "hero", dimension = "expression", value = "weap" }));
        if (type != "layer.hide") Assert.That(runtime.State.Table<Layer>().Get("hero").Variants, Is.Null);
        Execute(view, runtime, BuiltinEntryIds.ShowVariants, new { handleId = "hero", assetId = "atlas" });
        Assert.That(runtime.State.Table<Layer>().Get("hero").Variants!.Request["outfit"], Is.EqualTo("coat"));
    }

    [Test]
    public void Unknown_values_missing_catalog_and_cancelled_scope_do_not_partially_update_state()
    {
        var presenter = new Presenter();
        using var view = View(presenter);
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        Execute(view, runtime, BuiltinEntryIds.ShowVariants, Show());
        var before = runtime.CreateSnapshot().States["builtin.layers"].Payload.GetRawText();
        Assert.Throws<ArgumentException>(() => Execute(view, runtime, BuiltinEntryIds.SelectVariants, new { handleId = "hero", dimension = "expression", value = "unknown" }));
        Assert.Throws<InvalidOperationException>(() => Execute(view, runtime, BuiltinEntryIds.SelectVariants, new { handleId = "missing", dimension = "expression", value = "weap" }));
        Assert.Throws<InvalidDataException>(() => Execute(view, runtime, BuiltinEntryIds.ShowVariants, new { handleId = "hero", assetId = "missing" }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Execute(view, runtime, BuiltinEntryIds.ShowVariants, Show(), ct: cancellation.Token));
        Assert.That(runtime.CreateSnapshot().States["builtin.layers"].Payload.GetRawText(), Is.EqualTo(before));
        Assert.That(presenter.Replaced, Is.Empty);
    }

    [Test]
    public async Task Default_fallback_preserves_its_protection_across_snapshot_and_repeated_selection()
    {
        using var view = View(new());
        var runtime = new GameRuntime(null, "root", stateCatalog: view.StateCatalog);
        Execute(view, runtime, BuiltinEntryIds.ShowVariants, Show());
        Execute(view, runtime, BuiltinEntryIds.SelectVariants, new { handleId = "hero", dimension = "expression", value = "weap" });
        Execute(view, runtime, BuiltinEntryIds.SelectVariants, new { handleId = "hero", dimension = "outfit", value = "skirt" });
        Assert.That(runtime.State.Table<Layer>().Get("hero").Variants!.Reason, Is.EqualTo(ImageVariantResolutionReason.Default));
        var saved = runtime.CreateSnapshot();
        await using var engine = new GameEngine(new Graph { RootNodeId = "root", Nodes = [new Group { Id = "root" }] }, runtime, view);
        await engine.RestoreFromAsync(saved);
        var restored = runtime.State.Table<Layer>().Get("hero");
        Assert.That(restored.Variants!.ProtectedDimensions, Is.EqualTo(new[] { "outfit" }));
        Assert.That(restored.AssetId, Is.EqualTo("sprite-idle_coat"));
        Execute(view, runtime, BuiltinEntryIds.SelectVariants, new { handleId = "hero", dimension = "expression", value = "smile" });
        Assert.That(runtime.State.Table<Layer>().Get("hero").AssetId, Is.EqualTo("sprite-smile_skirt"));
    }

    [Test]
    public void Saved_resource_identity_uses_the_same_case_insensitive_rules_as_the_asset_manager()
    {
        using var view = View(new());
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        Execute(view, runtime, BuiltinEntryIds.ShowVariants, new { handleId = "hero", assetId = " ATLAS " });
        runtime.State.Table<Layer>().Get("hero").AssetId = "SPRITE-IDLE_COAT";
        Assert.DoesNotThrow(() => view.ValidateState(runtime, GameState.Restore(view.StateCatalog, runtime.CreateSnapshot().States)));
    }

    [TestCase("content")]
    [TestCase("actual")]
    [TestCase("request")]
    [TestCase("protection")]
    [TestCase("source-conflict")]
    [TestCase("version")]
    public async Task Invalid_restore_is_rejected_before_replacing_the_state_root(string fault)
    {
        var presenter = new Presenter();
        using var sourceView = View(presenter);
        var sourceRuntime = new GameRuntime(null, "root", stateCatalog: sourceView.StateCatalog);
        Execute(sourceView, sourceRuntime, BuiltinEntryIds.ShowVariants, new { handleId = "hero", assetId = "atlas", selection = new { expression = "weap" } });
        var snapshot = sourceRuntime.CreateSnapshot();
        var section = snapshot.States["builtin.layers"];
        var json = JsonNode.Parse(section.Payload.GetRawText())!;
        switch (fault)
        {
            case "actual": json[0]!["assetId"] = "wrong"; break;
            case "request": json[0]!["variants"]!["request"]!["expression"] = "unknown"; break;
            case "protection": json[0]!["variants"]!["protectedDimensions"] = new JsonArray("missing"); break;
            case "source-conflict": json[0]!["isVideo"] = true; break;
        }
        snapshot.States["builtin.layers"] = new(fault == "version" ? 1 : 2, JsonSerializer.SerializeToElement(json));
        using var targetView = View(new(), Catalog(fault == "content" ? "changed-image" : "sprite-weap"));
        var runtime = new GameRuntime(null, "root", stateCatalog: targetView.StateCatalog);
        var root = runtime.State;
        await using var engine = new GameEngine(new Graph { RootNodeId = "root", Nodes = [new Group { Id = "root" }] }, runtime, targetView);
        Assert.ThrowsAsync<InvalidDataException>(() => engine.RestoreFromAsync(snapshot));
        Assert.That(runtime.State, Is.SameAs(root));
        Assert.That(runtime.State.Table<Layer>().Values, Is.Empty);
    }

    [TestCase("{\"handleId\":\"hero\",\"assetId\":\"atlas\",\"selection\":{\"expression\":null}}")]
    [TestCase("{\"handleId\":\"hero\",\"assetId\":\"atlas\",\"selection\":{\"expression\":\"idle\",\"expression\":\"smile\"}}")]
    [TestCase("{\"handleId\":\"hero\",\"assetId\":\"atlas\",\"opacity\":2}")]
    [TestCase("{\"handleId\":\"hero\",\"assetId\":\"atlas\",\"transform\":{\"scaleX\":0}}")]
    [TestCase("{\"assetId\":\"atlas\"}")]
    public void Contract_rejects_bad_input_before_creating_a_layer(string json)
    {
        using var view = View(new());
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        using var arguments = JsonDocument.Parse(json);
        Assert.That(() => view.Dispatch(new PrimitiveEntry(BuiltinEntryIds.ShowVariants, arguments.RootElement), runtime, default), Throws.Exception);
        Assert.That(runtime.State.Table<Layer>().Values, Is.Empty);
    }

    private sealed class Presenter : ILayerPresenter
    {
        public List<LayerRenderRequest> Shown { get; } = [];
        public List<string> Replaced { get; } = [];
        public void ShowLayer(LayerRenderRequest request) => Shown.Add(request);
        public void ReplaceLayer(string handleId, string assetId) => Replaced.Add(assetId);
        public void HideLayer(string handleId) { }
        public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds) { }
    }

    private sealed class PendingAnimations : IAnimationPresenter
    {
        public TaskCompletionSource<AnimationOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken) => Completion.Task.WaitAsync(cancellationToken);
        public Task<TimelinePlayResult> PlayTimelineAsync(TimelineDefinition timeline, CancellationToken cancellationToken, Func<double, Task>? beforeFrame = null) => throw new NotSupportedException();
        public bool CompleteAnimationImmediately(string playbackHandleId) => Completion.TrySetResult(AnimationOutcome.Skipped);
    }
}
