using GalNet.Avalonia.GameView.Presentation;
using GalNet.Core.Scene;
using GalNet.Rendering.Scene;
using SkiaSharp;

namespace GeneralTest.Scene;

public sealed class TextureEffectsTests
{
    [Test]
    public void Default_catalog_exposes_layer_and_scene_color_grade_with_shared_parameter_contract()
    {
        var catalog = AvaloniaEffectRuntime.CreateDefaultCatalog();

        Assert.Multiple(() =>
        {
            Assert.That(catalog.TryGet("layer.colorGrade", out var layer), Is.True);
            Assert.That(catalog.TryGet("scene.colorGrade", out var scene), Is.True);
            Assert.That(layer.Stage, Is.EqualTo(EffectStage.Layer));
            Assert.That(scene.Stage, Is.EqualTo(EffectStage.ScenePost));
            Assert.That(layer.Parameters.Select(parameter => parameter.Name), Is.EqualTo(scene.Parameters.Select(parameter => parameter.Name)));
            Assert.That(catalog.Validate("scene.colorGrade", "", "{\"brightness\":2}"), Has.Some.Contains("outside its supported range"));
        });
    }

    [Test]
    public void Color_grade_shader_applies_brightness_and_preserves_alpha()
    {
        using var source = Solid(SKColors.Red);
        using var target = new SKBitmap(source.Info);
        var instance = new SceneEffectInstance("grade", new LayerColorGradeTextureEffectFactory(), "layer", 0, "{\"brightness\":-0.5,\"saturation\":0,\"hue\":0}");

        instance.Effect.Render(new SKCanvas(target), source, instance);
        var result = target.GetPixel(2, 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.Red, Is.LessThan(200));
            Assert.That(result.Alpha, Is.EqualTo(255));
        });
    }

    [Test]
    public void Color_grade_shader_desaturates_and_rotates_hue_from_the_same_texture_pass()
    {
        using var source = Solid(SKColors.Red);
        using var desaturated = new SKBitmap(source.Info);
        using var hueRotated = new SKBitmap(source.Info);
        var factory = new LayerColorGradeTextureEffectFactory();
        var gray = new SceneEffectInstance("gray", factory, "layer", 0, "{\"saturation\":-1}");
        var hue = new SceneEffectInstance("hue", factory, "layer", 0, "{\"hue\":120}");

        gray.Effect.Render(new SKCanvas(desaturated), source, gray);
        hue.Effect.Render(new SKCanvas(hueRotated), source, hue);
        var grayscale = desaturated.GetPixel(2, 2);
        var rotated = hueRotated.GetPixel(2, 2);

        Assert.Multiple(() =>
        {
            Assert.That(grayscale.Red, Is.EqualTo(grayscale.Green).Within(1));
            Assert.That(grayscale.Green, Is.EqualTo(grayscale.Blue).Within(1));
            Assert.That(rotated, Is.Not.EqualTo(SKColors.Red));
        });
    }

    [Test]
    public void Blinds_shader_uses_progress_as_the_single_reveal_input()
    {
        using var source = Solid(SKColors.White);
        using var hidden = new SKBitmap(source.Info);
        using var revealed = new SKBitmap(source.Info);
        var factory = new BlindsTextureEffectFactory();
        var closed = new SceneEffectInstance("closed", factory, "layer", 0, "{\"bladeCount\":1,\"orientation\":\"Vertical\"}");
        var open = new SceneEffectInstance("open", factory, "layer", 0, "{\"bladeCount\":1,\"orientation\":\"Vertical\"}");
        closed.SetAnimatedValue("progress", 0);
        open.SetAnimatedValue("progress", 1);

        closed.Effect.Render(new SKCanvas(hidden), source, closed);
        open.Effect.Render(new SKCanvas(revealed), source, open);

        Assert.Multiple(() =>
        {
            Assert.That(hidden.GetPixel(2, 2).Alpha, Is.EqualTo(0));
            Assert.That(revealed.GetPixel(2, 2).Alpha, Is.EqualTo(255));
        });
    }

    private static SKBitmap Solid(SKColor color)
    {
        var bitmap = new SKBitmap(8, 8);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        return bitmap;
    }
}
