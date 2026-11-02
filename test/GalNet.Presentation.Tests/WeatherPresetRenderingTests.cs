using System.Text.Json;
using Avalonia.Media;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Rendering.Scene;
using SkiaSharp;

namespace GeneralTest.Scene;

public sealed class WeatherPresetRenderingTests
{
    private static string Root()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent!;
        return root.FullName;
    }

    private static string Asset(string id) => Directory.GetFiles(Path.Combine(Root(), "GameTestCase/Assets"), "*.meta", SearchOption.AllDirectories)
        .Single(path =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("id").GetString() == id;
        })[..^5];

    private static ParticleEmitterDefinition Definition(string weather)
    {
        var group = JsonSerializer.Deserialize<GroupDocument>(File.ReadAllText(Path.Combine(Root(), "GameTestCase/Graph/groups/weather.rawgalgroup")))!;
        group.Entries = [group.Entries.Single(entry => entry.Id == "weather-" + weather)];
        var clip = GalgroupCompiler.Compile(group, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())).Document.Entries.Single();
        var parameters = clip.TypeId == "particle.play" ? clip.Arguments.GetProperty("parameters")
            : clip.Arguments.GetProperty("timeline").GetProperty("events")[0].GetProperty("parameters").GetProperty("parameters");
        return ParticleEmitterDefinition.FromJson(parameters.GetRawText());
    }

    private static SKBitmap Render(string weather, double seconds)
    {
        var definition = Definition(weather);
        using var texture = new SceneTexture(new DrawingImage(), SKBitmap.Decode(Asset(definition.ParticleTexture)));
        if (weather == "Rain") Assert.That(texture.SkBitmap!.Height / (float)texture.SkBitmap.Width, Is.GreaterThanOrEqualTo(16));
        using var emitter = new ParticleEmitter("weather", texture, definition, 100);
        for (var frame = 0; frame < (int)Math.Round(seconds * 60); frame++)
            emitter.Update(new SceneFrameContext(TimeSpan.FromSeconds((frame + 1) / 60d), TimeSpan.FromSeconds(1d / 60), new SKSize(1920, 1080)));
        var result = new SKBitmap(1920, 1080); result.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(result);
        emitter.Render(new SceneRenderContext(canvas, new SKSize(1920, 1080), false));
        return result;
    }

    [TestCase("Rain")]
    [TestCase("RadialRain")]
    [TestCase("Snow")]
    [TestCase("Petals")]
    [TestCase("Dust")]
    [TestCase("Fireflies")]
    public void Actual_weather_enters_both_halves_from_just_above_the_viewport(string weather)
    {
        using var result = Render(weather, .75);
        var left = false; var right = false; var top = false; var bottom = false;
        for (var y = 0; y < result.Height; y++)
            for (var x = 0; x < result.Width; x++)
            {
                if (result.GetPixel(x, y).Alpha == 0) continue;
                if (x < result.Width / 2) left = true; else right = true;
                if (y < result.Height / 2) top = true;
                else bottom = true;
            }
        Assert.That(left && right && top, Is.True, "Weather should enter the viewport promptly across its full width.");
        if (weather is "Snow" or "Petals") Assert.That(bottom, Is.False, "Falling weather should originate above the frame, not appear midway down it.");
    }

    [Test]
    public void Petals_are_pink_and_rain_is_translucent_in_actual_rendered_pixels()
    {
        using var petals = Render("Petals", 2);
        var visible = petals.Pixels.Where(pixel => pixel.Alpha > 32).ToArray();
        Assert.That(visible.Length, Is.GreaterThan(0));
        Assert.That(visible.Count(pixel => pixel.Red > pixel.Blue && pixel.Blue > pixel.Green), Is.GreaterThan(visible.Length * .95));
        using var rain = Render("Rain", 2);
        var drops = rain.Pixels.Where(pixel => pixel.Alpha > 0).ToArray();
        Assert.That(drops.Length, Is.GreaterThan(0));
        Assert.That(drops.Max(pixel => pixel.Alpha), Is.LessThan(180));
    }

    [TestCase("Rain")]
    [TestCase("Snow")]
    [TestCase("Petals")]
    public void Actual_weather_keeps_emitting_visible_particles_for_sixty_seconds(string weather)
    {
        var definition = Definition(weather);
        using var texture = new SceneTexture(new DrawingImage(), SKBitmap.Decode(Asset(definition.ParticleTexture)));
        using var emitter = new ParticleEmitter("weather", texture, definition, 100);
        using var strip = new SKBitmap(1920, 120);
        using var canvas = new SKCanvas(strip);
        for (var frame = 0; frame < 1800; frame++)
        {
            emitter.Update(new SceneFrameContext(TimeSpan.FromSeconds((frame + 1) / 30d), TimeSpan.FromSeconds(1d / 30), new SKSize(1920, 1080)));
            if (frame < 60 || frame % 15 != 0) continue;
            canvas.Clear(SKColors.Transparent);
            emitter.Render(new SceneRenderContext(canvas, new SKSize(1920, 1080), false));
            Assert.That(strip.Pixels.Any(pixel => pixel.Alpha > 0), Is.True, $"No fresh {weather} particles at {frame / 30d:0.0}s.");
        }
    }

    [Test]
    public void Rain_streaks_follow_their_velocity_and_radial_rain_uses_outward_motion()
    {
        var rain = Definition("Rain"); var radial = Definition("RadialRain");
        Assert.That(rain.InitialModule.AlignToVelocity, Is.True);
        Assert.That(rain.InitialModule.VelocityX.Min, Is.GreaterThan(0));
        Assert.That(radial.InitialModule.AlignToVelocity, Is.True);
        Assert.That(radial.MotionModule.RadialVelocity, Is.GreaterThan(0));
        using var texture = new SceneTexture(new DrawingImage(), SKBitmap.Decode(Asset(rain.ParticleTexture)));
        var definition = rain with { EmissionRate = 0, Initial = rain.InitialModule with {
            VelocityX = new(100, 100), VelocityY = new(100, 100), Size = new(.1f, .1f) },
            Shape = new(ParticleShapeKind.Point, 100, 100), Lifetime = new() { Opacity = [new(0, 1)] } };
        using var emitter = new ParticleEmitter("slant", texture, definition, 0, initialBurstCount: 1);
        emitter.Update(new(TimeSpan.FromSeconds(.01), TimeSpan.FromSeconds(.01), new SKSize(200, 200)));
        using var bitmap = new SKBitmap(200, 200); bitmap.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bitmap); emitter.Render(new(canvas, new SKSize(200, 200), false));
        var minX = 200; var maxX = 0; var minY = 200; var maxY = 0;
        for (var y = 0; y < 200; y++) for (var x = 0; x < 200; x++)
            if (bitmap.GetPixel(x, y).Alpha > 0) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        Assert.That(maxX - minX, Is.GreaterThan(10));
        Assert.That(maxY - minY, Is.GreaterThan(10));
    }

    [Test, Explicit("Writes a native-size weather contact sheet for visual inspection.")]
    public void WriteWeatherPreview()
    {
        using var background = SKBitmap.Decode(Asset("a1000000000000000000000000000001"));
        using var sheet = new SKBitmap(1920, 1560);
        using var canvas = new SKCanvas(sheet); canvas.Clear(SKColors.DarkSlateGray);
        using var font = new SKFont(SKTypeface.Default, 30);
        using var paint = new SKPaint { Color = SKColors.White };
        var weather = new[] { "Rain", "RadialRain", "Snow", "Petals" };
        for (var i = 0; i < weather.Length; i++)
        {
            var y = i * 390;
            canvas.DrawText(weather[i], 12, y + 32, font, paint);
            // A crop at native scale keeps the fine rain streaks reviewable.
            var target = new SKRect(0, y + 40, 1920, y + 390);
            canvas.DrawBitmap(background, target);
            using var frame = Render(weather[i], weather[i] == "Rain" ? 2 : 8);
            canvas.DrawBitmap(frame, new SKRect(0, 350, 1920, 700), target);
        }
        var directory = Path.Combine(Root(), "output/weather-polish"); Directory.CreateDirectory(directory);
        using var image = SKImage.FromBitmap(sheet); using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, "weather.png"), bytes.ToArray());
    }
}
