using Avalonia;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Assets;
using GalNet.Rendering.Scene;
using SkiaSharp;

namespace GeneralTest.Scene;

public sealed class DefaultVisualRenderingTests
{
    [Test, Explicit("Writes the feature's CPU rendering contact sheet for visual inspection.")]
    public void WriteDefaultVisualPreview()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
        var root = directory.FullName;
        var imagePath = Directory.GetFiles(Path.Combine(root, "GameTestCase/Assets"), "*.meta", SearchOption.AllDirectories).Single(path =>
        {
            using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return metadata.RootElement.GetProperty("id").GetString() == "a1000000000000000000000000000001";
        })[..^5];
        using var texture = new SceneTexture(new Avalonia.Media.DrawingImage(), SKBitmap.Decode(imagePath));
        var layer = new SceneLayerItem { HandleId = "background", Texture = texture, DisplayMode = LayerDisplayMode.Fill, Z = 50 };
        var overlay = new SceneLayerItem { HandleId = "lines", Color = "#00000000", Z = 100 };
        var plan = SceneRenderPlan.Create([new(layer, 0), new(overlay, 1)]);
        var programs = new List<SkiaShaderEffectProgramLoadResult>();
        SceneEffectInstance Effect(string name, EffectStage stage, string target, string parameters)
        {
            var program = DefaultVisualShaderTests.Load(name); programs.Add(program);
            return new(name, new ShaderProgramTextureEffectFactory(name, stage, new(name), program), target, 0, parameters);
        }
        try
        {
            var panels = new (string Title, SceneEffectInstance[] Effects)[]
            {
                ("Original", []),
                ("Bloom + warm grade", [Effect("Bloom", EffectStage.ScenePost, "", "{\"strength\":3,\"radius\":24}"), Effect("ColorGrade", EffectStage.ScenePost, "", "{\"temperature\":0.5}")]),
                ("Vignette", [Effect("Vignette", EffectStage.ScenePost, "", "{\"strength\":0.9,\"radius\":0.2}")]),
                ("Layer Z depth blur", [Effect("DepthBlur", EffectStage.Layer, "background", "{\"falloff\":1,\"maxRadius\":16}")]),
                ("Dynamic radial lines", [Effect("SpeedLines", EffectStage.Layer, "lines", "{\"time\":0.137}")]),
                ("Dynamic directional lines", [Effect("SpeedLines", EffectStage.Layer, "lines", "{\"mode\":\"Directional\",\"time\":0.137}")])
            };
            using var sheet = new SKBitmap(1440, 600);
            using var canvas = new SKCanvas(sheet); canvas.Clear(new SKColor(20, 24, 32));
            using var font = new SKFont(SKTypeface.Default, 18);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            for (var i = 0; i < panels.Length; i++)
            {
                var x = i % 3 * 480; var y = i / 3 * 300;
                using var scene = SceneRenderPipeline.Render(plan, panels[i].Effects, null, new Size(480, 270));
                canvas.DrawText(panels[i].Title, x + 12, y + 22, font, paint);
                canvas.DrawBitmap(scene, x, y + 30);
                foreach (var effect in panels[i].Effects) effect.Dispose();
            }
            using var image = SKImage.FromBitmap(sheet);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            Directory.CreateDirectory(Path.Combine(root, "artifacts"));
            File.WriteAllBytes(Path.Combine(root, "artifacts/visual-library-preview.png"), encoded.ToArray());
        }
        finally { foreach (var program in programs) program.RuntimeEffect?.Dispose(); }
    }

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
