using GalNet.Contracts.Builtins.Scene;
using GalNet.Rendering.Scene;
using SkiaSharp;

namespace GeneralTest.Scene;

public sealed class PresentationShaderPixelsTests
{
    [Test, Explicit("Writes the feature contact sheet to output for visual inspection.")]
    public void WritePresentationLibraryPreview()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent!;
        var asset = Directory.GetFiles(Path.Combine(root.FullName, "GameTestCase/Assets"), "*.meta", SearchOption.AllDirectories).Single(path =>
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.GetProperty("id").GetString() == "a1000000000000000000000000000001";
        })[..^5];
        using var image = SKBitmap.Decode(asset); using var source = new SKBitmap(480, 270);
        using (var canvas = new SKCanvas(source)) canvas.DrawBitmap(image, new SKRect(0, 0, 480, 270));
        var examples = new[] {
            ("Distortion", "{\"strength\":12,\"mode\":\"Ripple\",\"time\":0.3}"),
            ("RadialBlur", "{\"strength\":0.4}"), ("Film", "{\"strength\":0.9,\"grain\":0.3,\"time\":0.13}"),
            ("Glitch", "{\"strength\":0.8,\"time\":0.17}"), ("Pixelate", "{\"blockSize\":16}"),
            ("Spotlight", "{\"strength\":0.9,\"radius\":0.2}"),
            ("ShapeMask", "{\"mode\":\"Ellipse\",\"width\":0.7,\"height\":0.7,\"feather\":0.05,\"overlay\":true}"),
            ("Mood", "{\"mode\":\"Memory\",\"strength\":0.9}") };
        using var sheet = new SKBitmap(1920, 600); using var target = new SKCanvas(sheet); target.Clear(new SKColor(20, 24, 32));
        using var font = new SKFont(SKTypeface.Default, 20); using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        for (var i = 0; i < examples.Length; i++)
        {
            var x = i % 4 * 480; var y = i / 4 * 300;
            target.DrawText(examples[i].Item1, x + 12, y + 23, font, paint);
            using var result = Draw(examples[i].Item1, examples[i].Item2, source);
            target.DrawBitmap(source, x, y + 30); target.DrawBitmap(result, x, y + 30);
        }
        var directory = Path.Combine(root.FullName, "output/presentation-library"); Directory.CreateDirectory(directory);
        using var output = SKImage.FromBitmap(sheet); using var bytes = output.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, "preview.png"), bytes.ToArray());
    }

    [Test]
    public void Transparent_overlay_rendering_preserves_the_underlying_scene_in_empty_regions()
    {
        using var output = SceneRenderPipeline.Render(SceneRenderPlan.Empty, [], null, new Avalonia.Size(32, 16),
            new SceneRenderOptions(SceneRenderBudget.Default) { TransparentBackground = true });
        Assert.That(output.Pixels.All(pixel => pixel.Alpha == 0), Is.True);
        using var opaque = SceneRenderPipeline.Render(SceneRenderPlan.Empty, [], null, new Avalonia.Size(32, 16));
        Assert.That(opaque.Pixels.All(pixel => pixel.Alpha == 255), Is.True);
    }
    private static SKBitmap Draw(string name, string parameters, SKBitmap source)
    {
        var program = DefaultVisualShaderTests.Load(name);
        using var compiled = program.RuntimeEffect;
        Assert.That(program.IsUsable, Is.True, string.Join(";", program.Diagnostics));
        using var effect = new SceneEffectInstance("fx", new ShaderProgramTextureEffectFactory(name, EffectStage.Layer, new(name), program), "layer", 0, parameters);
        var output = new SKBitmap(source.Info); output.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(output); effect.Effect.Render(canvas, source, effect);
        return output;
    }

    [Test, Explicit("Writes eyelid animation samples for visual inspection.")]
    public void WriteEyelidPreview()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent!;
        var asset = Directory.GetFiles(Path.Combine(root.FullName, "GameTestCase/Assets"), "*.meta", SearchOption.AllDirectories).Single(path =>
            File.ReadAllText(path).Contains("a1000000000000000000000000000001", StringComparison.Ordinal))[..^5];
        using var original = SKBitmap.Decode(asset);
        using var source = new SKBitmap(320, 180);
        using (var canvas = new SKCanvas(source)) canvas.DrawBitmap(original, new SKRect(0, 0, 320, 180));
        using var sheet = new SKBitmap(1280, 420);
        using var target = new SKCanvas(sheet); target.Clear(SKColors.DarkSlateGray);
        using var font = new SKFont(SKTypeface.Default, 18);
        using var paint = new SKPaint { Color = SKColors.White };
        var progress = new[] { 0f, .08f, .3f, .5f, .65f, .8f, .98f, 1f };
        for (var i = 0; i < progress.Length; i++)
        {
            using var frame = Draw("ShapeMask", System.Text.Json.JsonSerializer.Serialize(new { mode = "Eyelid", progress = progress[i], feather = .025f, overlay = true }), source);
            var x = i % 4 * 320; var y = i / 4 * 210;
            target.DrawText($"Open {progress[i]:0.00}", x + 8, y + 22, font, paint);
            target.DrawBitmap(frame, x, y + 30);
        }
        var directory = Path.Combine(root.FullName, "output/presentation-polish"); Directory.CreateDirectory(directory);
        using var image = SKImage.FromBitmap(sheet); using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, "eyelids.png"), bytes.ToArray());
    }

    [TestCase(64, 36, 0f)]
    [TestCase(127, 71, .025f)]
    [TestCase(321, 181, .1f)]
    public void Eyelid_expands_and_fades_continuously_including_all_four_edges(int width, int height, float feather)
    {
        using var source = new SKBitmap(width, height); source.Erase(SKColors.Transparent);
        SKBitmap Eye(float progress) => Draw("ShapeMask", System.Text.Json.JsonSerializer.Serialize(new
            { mode = "Eyelid", progress, feather, overlay = true }), source);
        using var closed = Eye(0);
        Assert.That(closed.Pixels.All(pixel => pixel.Alpha == 255), Is.True, "Closed eyelids must cover every edge pixel.");
        using var almostClosed = Eye(.001f);
        Assert.That(almostClosed.Pixels.Min(pixel => pixel.Alpha), Is.GreaterThanOrEqualTo(253));
        long previous = closed.Pixels.Sum(pixel => (long)pixel.Alpha);
        foreach (var progress in new[] { .08f, .3f, .6f, .7f, .8f, .9f, .98f, .999f, 1f })
        {
            using var frame = Eye(progress);
            var coverage = frame.Pixels.Sum(pixel => (long)pixel.Alpha);
            Assert.That(coverage, Is.LessThanOrEqualTo(previous), $"Opening reversed at {progress}.");
            if (progress >= .999f) Assert.That(frame.Pixels.Max(pixel => pixel.Alpha), Is.LessThanOrEqualTo(1));
            previous = coverage;
        }
        Assert.That(previous, Is.Zero);
    }

    [TestCase(127.3, 71.7)]
    [TestCase(321, 181)]
    public void Closed_feathered_overlay_covers_every_output_pixel_at_fractional_viewport_sizes(double width, double height)
    {
        var program = DefaultVisualShaderTests.Load("ShapeMask");
        using var compiled = program.RuntimeEffect;
        using var effect = new SceneEffectInstance("mask", new ShaderProgramTextureEffectFactory("mask", EffectStage.Layer, new("mask"), program),
            "overlay", 0, """{"mode":"Eyelid","progress":0,"feather":0.1,"overlay":true}""");
        var layer = new SceneLayerItem { HandleId = "overlay", Color = "#00000000" };
        using var result = SceneRenderPipeline.Render(SceneRenderPlan.Create([new(layer, 0)]), [effect], null,
            new Avalonia.Size(width, height), new Avalonia.PixelSize(233, 131),
            new SceneRenderOptions(SceneRenderBudget.Default) { TransparentBackground = true });
        Assert.That(result.Pixels.All(pixel => pixel.Alpha == 255 && pixel.Red == 0), Is.True);
    }

    [TestCase("Distortion", "{\"strength\":0}")]
    [TestCase("RadialBlur", "{\"strength\":0}")]
    [TestCase("Film", "{\"strength\":0}")]
    [TestCase("Glitch", "{\"strength\":0}")]
    [TestCase("Pixelate", "{\"blockSize\":1}")]
    [TestCase("Spotlight", "{\"strength\":0}")]
    [TestCase("Mood", "{\"strength\":0}")]
    public void Neutral_parameters_preserve_opaque_and_transparent_pixels(string name, string parameters)
    {
        using var source = new SKBitmap(32, 16); source.Erase(SKColors.Transparent);
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++) source.SetPixel(x, y, new SKColor((byte)(x * 15), (byte)(y * 15), 100));
        using var result = Draw(name, parameters, source);
        Assert.That(result.Pixels, Is.EqualTo(source.Pixels));
    }

    [Test]
    public void Feather_and_inversion_have_distinct_alpha_and_eyelid_endpoints_are_closed_or_open()
    {
        using var source = new SKBitmap(64, 64); source.Erase(SKColors.White);
        using var hard = Draw("ShapeMask", """{"mode":"Ellipse","width":0.5,"height":0.5,"feather":0}""", source);
        using var soft = Draw("ShapeMask", """{"mode":"Ellipse","width":0.5,"height":0.5,"feather":0.1}""", source);
        using var inverse = Draw("ShapeMask", """{"mode":"Ellipse","width":0.5,"height":0.5,"invert":true}""", source);
        Assert.That(hard.Pixels.All(p => p.Alpha is 0 or 255), Is.True);
        Assert.That(soft.Pixels.Any(p => p.Alpha is > 0 and < 255), Is.True);
        Assert.That(inverse.GetPixel(32, 32).Alpha, Is.Zero); Assert.That(inverse.GetPixel(0, 0).Alpha, Is.EqualTo(255));
        using var closed = Draw("ShapeMask", """{"mode":"Eyelid","progress":0,"overlay":true,"color":"#000000"}""", source);
        Assert.That(closed.Pixels.All(p => p.Alpha == 255 && p.Red == 0), Is.True);
        using var open = Draw("ShapeMask", """{"mode":"Letterbox","progress":0,"overlay":true}""", source);
        Assert.That(open.Pixels, Is.EqualTo(source.Pixels));
    }
}
