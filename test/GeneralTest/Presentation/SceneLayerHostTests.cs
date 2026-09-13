using System.Collections.ObjectModel;
using Avalonia.Media;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Core.Scene;
using GalNet.Rendering.Scene;
using SkiaSharp;

namespace GeneralTest.Presentation;

public sealed class SceneLayerHostTests
{
    [Test]
    public void Missing_layer_diagnostics_are_reported_once_per_asset()
    {
        var assetId = $"missing-{Guid.NewGuid():N}";

        Assert.That(LayerImageFallback.ShouldReport(assetId), Is.True);
        Assert.That(LayerImageFallback.ShouldReport(assetId), Is.False);
    }

    [Test]
    public void Missing_resource_fallback_is_a_renderable_scene_layer()
    {
        // DrawingImage is the platform-neutral test stand-in for the embedded
        // fallback bitmap; Bitmap construction requires an Avalonia render backend.
        using var fallback = new SceneTexture(new DrawingImage());
        var item = new SceneLayerItem
        {
            HandleId = "missing-layer-demo",
            Texture = fallback,
            DisplayMode = LayerDisplayMode.Native,
            ScaleX = 0.18,
            ScaleY = 0.18,
            Z = 5
        };
        var host = new SceneLayerHost { ItemsSource = new[] { item } };

        Assert.Multiple(() =>
        {
            Assert.That(fallback.AvaloniaImage, Is.TypeOf<DrawingImage>());
            Assert.That(host.RenderPlan.Items.Select(entry => entry.Layer.Texture), Is.EqualTo(new[] { fallback }));
        });
    }

    [Test]
    public void Items_build_a_stable_render_plan_by_z_order()
    {
        var background = new SceneLayerItem { HandleId = "background", Z = 0, DisplayMode = LayerDisplayMode.Fill };
        var character = new SceneLayerItem { HandleId = "character", X = 700, Y = 250, Z = 20, ScaleX = 1.2, ScaleY = 1 };
        var host = new SceneLayerHost { ItemsSource = new ObservableCollection<SceneLayerItem> { background, character } };

        Assert.That(host.RenderPlan.Items.Select(entry => entry.Layer.HandleId), Is.EqualTo(["background", "character"]));
        Assert.That(host.RenderPlan.Items.Select(entry => entry.Order), Is.EqualTo([0, 20]));

        character.Z = -1;
        Assert.That(host.RenderPlan.Items.Select(entry => entry.Layer.HandleId), Is.EqualTo(["character", "background"]));
        Assert.That(host.RenderPlan.Items.Select(entry => entry.Order), Is.EqualTo([-1, 0]));
    }

    [TestCase(LayerDisplayMode.Native)]
    [TestCase(LayerDisplayMode.Tile)]
    [TestCase(LayerDisplayMode.Fill)]
    [TestCase(LayerDisplayMode.Uniform)]
    [TestCase(LayerDisplayMode.UniformToFill)]
    public void Every_display_mode_can_be_added_to_the_render_plan(LayerDisplayMode displayMode)
    {
        var item = new SceneLayerItem { HandleId = "layer", DisplayMode = displayMode, ScaleX = 2, ScaleY = 1 };
        var host = new SceneLayerHost { ItemsSource = new[] { item } };

        Assert.That(host.RenderPlan.Items, Has.Count.EqualTo(1));
        Assert.That(item.DisplayMode, Is.EqualTo(displayMode));
    }

    [Test]
    public void Scene_renderables_are_interleaved_with_layers_by_the_shared_z_order()
    {
        var layer = new SceneLayerItem { HandleId = "red-layer", Color = "#ff0000", Z = 10, DisplayMode = LayerDisplayMode.Fill };
        var plan = SceneRenderPlan.Create([new SceneRenderEntry(layer, 0)]);
        var blueBehind = new SolidRenderable("blue-behind", 0, SKColors.Blue);
        var blueAhead = new SolidRenderable("blue-ahead", 20, SKColors.Blue);

        using var behind = SceneRenderPipeline.Render(plan, [], [blueBehind], new Avalonia.Size(8, 8));
        using var ahead = SceneRenderPipeline.Render(plan, [], [blueAhead], new Avalonia.Size(8, 8));

        Assert.Multiple(() =>
        {
            Assert.That(behind.GetPixel(4, 4), Is.EqualTo(SKColors.Red));
            Assert.That(ahead.GetPixel(4, 4), Is.EqualTo(SKColors.Blue));
        });
    }

    [Test]
    public void Effect_pass_budget_preserves_the_unmodified_scene_and_records_a_diagnostic()
    {
        var diagnostics = new SceneRenderDiagnostics();
        var effect = new SceneEffectInstance("over-budget", new TestEffectFactory("fill", (canvas, _, _) => canvas.DrawColor(SKColors.Blue)), "", 0, "{}");
        var plan = SceneRenderPlan.Create([new SceneRenderEntry(new SolidRenderable("base", 0, SKColors.Red), 0)]);

        using var scene = SceneRenderPipeline.Render(
            plan,
            [effect],
            null,
            new Avalonia.Size(8, 8),
            new SceneRenderOptions(new SceneRenderBudget(MaxEffectPasses: 0), diagnostics));

        var snapshot = diagnostics.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(scene.GetPixel(4, 4), Is.EqualTo(SKColors.Red));
            Assert.That(snapshot.LastEffectPassCount, Is.EqualTo(0));
            Assert.That(snapshot.Events.Select(@event => @event.Code), Does.Contain("effect.skipped.pass-budget"));
        });
    }

    [Test]
    public void Failed_effect_uses_identity_output_and_reports_the_failure()
    {
        var diagnostics = new SceneRenderDiagnostics();
        var effect = new SceneEffectInstance("broken", new TestEffectFactory("broken", (_, _, _) => throw new InvalidOperationException("intentional")), "", 0, "{}");
        var plan = SceneRenderPlan.Create([new SceneRenderEntry(new SolidRenderable("base", 0, SKColors.Red), 0)]);

        using var scene = SceneRenderPipeline.Render(plan, [effect], null, new Avalonia.Size(8, 8), new SceneRenderOptions(SceneRenderBudget.Default, diagnostics));

        var snapshot = diagnostics.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(scene.GetPixel(4, 4), Is.EqualTo(SKColors.Red));
            Assert.That(snapshot.LastEffectPassCount, Is.EqualTo(1));
            Assert.That(snapshot.LastPeakIntermediateTextureBytes, Is.EqualTo(8 * 8 * 4));
            Assert.That(snapshot.Events.Select(@event => @event.Code), Does.Contain("effect.identity-on-failure"));
        });
    }

    private sealed class SolidRenderable(string handleId, double z, SKColor color) : ISceneRenderable
    {
        public string HandleId { get; } = handleId;
        public double Z { get; } = z;
        public void Render(SceneRenderContext context) => context.Canvas.DrawColor(color);
    }

    private sealed class TestEffectFactory(string id, Action<SKCanvas, SKBitmap, SceneEffectInstance> render) : ITextureEffectFactory
    {
        public EffectDefinition Definition { get; } = new(id, EffectStage.ScenePost, [], []);
        public ITextureEffect Create() => new TestEffect(render);
    }

    private sealed class TestEffect(Action<SKCanvas, SKBitmap, SceneEffectInstance> render) : ITextureEffect
    {
        public void Render(SKCanvas target, SKBitmap source, SceneEffectInstance instance) => render(target, source, instance);
    }
}
