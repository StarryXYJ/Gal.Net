using Avalonia;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Rendering.Scene;
using SkiaSharp;

namespace GeneralTest.Scene;

public sealed class DefaultVisualRenderingTests
{
    [Test]
    public void Depth_blur_uses_current_layer_z_on_every_draw()
    {
        var program = DefaultVisualShaderTests.Load("DepthBlur");
        using var compiled = program.RuntimeEffect;
        var factory = new ShaderProgramTextureEffectFactory("depth", EffectStage.Layer, new("depth"), program);
        using var effect = new SceneEffectInstance("depth", factory, "target", 0, "{\"focusZ\":0,\"falloff\":1,\"maxRadius\":16}");
        var layer = new SplitLayer();
        var plan = SceneRenderPlan.Create([new(layer, 0)]);
        using var focused = SceneRenderPipeline.Render(plan, [effect], null, new Size(64, 32));
        layer.Z = 50;
        using var blurred = SceneRenderPipeline.Render(plan, [effect], null, new Size(64, 32));
        Assert.That(focused.GetPixel(29, 16).Red, Is.EqualTo(255));
        Assert.That(blurred.GetPixel(29, 16).Red, Is.LessThan(240));
        layer.Z = 0;
        using var restored = SceneRenderPipeline.Render(plan, [effect], null, new Size(64, 32));
        Assert.That(restored.GetPixel(29, 16), Is.EqualTo(focused.GetPixel(29, 16)));
    }

    [TestCase("Radial")]
    [TestCase("Directional")]
    public void Speed_lines_generate_visible_transparent_animation(string mode)
    {
        var program = DefaultVisualShaderTests.Load("SpeedLines");
        using var compiled = program.RuntimeEffect;
        var factory = new ShaderProgramTextureEffectFactory("lines", EffectStage.Layer, new("lines"), program);
        using var effect = new SceneEffectInstance("lines", factory, "target", 0, "{\"mode\":\"" + mode + "\"}");
        using var source = new SKBitmap(128, 128);
        source.Erase(SKColors.Transparent);
        using var first = Draw(effect, source);
        effect.SetAnimatedValue("time", .137f);
        using var second = Draw(effect, source);
        Assert.That(first.Pixels.Any(pixel => pixel.Alpha > 0), Is.True);
        Assert.That(first.Pixels.Any(pixel => pixel.Alpha == 0), Is.True);
        Assert.That(first.Pixels.Zip(second.Pixels).Count(pair => pair.First != pair.Second), Is.GreaterThan(100));
    }

    [Test]
    public void Mask_texture_is_scaled_to_canvas_and_lease_is_released_once()
    {
        var program = DefaultVisualShaderTests.Load("MaskReveal");
        using var compiled = program.RuntimeEffect;
        var factory = new ShaderProgramTextureEffectFactory("mask", EffectStage.Layer, new("mask"), program);
        using var bitmap = new SKBitmap(2, 1);
        bitmap.SetPixel(0, 0, SKColors.Black);
        bitmap.SetPixel(1, 0, SKColors.White);
        using var texture = new SceneTexture(new Avalonia.Media.DrawingImage(), bitmap.Copy());
        var released = 0;
        var bindings = new ShaderTextureBindings();
        bindings.Add("mask", new AssetHandle<SceneTexture>("mask-id", "sprite", texture, () => released++));
        using var effect = new SceneEffectInstance("mask", factory, "target", 0, "{}", textures: bindings);
        effect.SetAnimatedValue("progress", .5f);
        using var source = new SKBitmap(64, 32);
        source.Erase(SKColors.White);
        using var result = Draw(effect, source);
        Assert.That(result.GetPixel(4, 16).Alpha, Is.EqualTo(255));
        Assert.That(result.GetPixel(60, 16).Alpha, Is.EqualTo(0));
        effect.Dispose();
        effect.Dispose();
        Assert.That(released, Is.EqualTo(1));
    }

    private static SKBitmap Draw(SceneEffectInstance effect, SKBitmap source)
    {
        var target = new SKBitmap(source.Info);
        target.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(target);
        effect.Effect.Render(canvas, source, effect);
        return target;
    }

    private sealed class SplitLayer : ILayerEffectTarget
    {
        public string HandleId => "target";
        public string EffectTargetHandleId => HandleId;
        public double Z { get; set; }
        public void Render(SceneRenderContext context)
        {
            context.Canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { Color = SKColors.White };
            context.Canvas.DrawRect(0, 0, 32, 32, paint);
        }
    }
}
