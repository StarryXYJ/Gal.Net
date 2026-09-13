using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Scene;
using GalNet.Core.View;
using GalNet.Rendering.Scene;
using SkiaSharp;

namespace GeneralTest.Scene;

public sealed class TextureEffectsTests
{
    [Test]
    public void Program_factory_derives_layer_and_scene_contract_from_one_project_shader()
    {
        var layer = CreateFactory(ColorGradeSource, EffectStage.Layer).Definition;
        var scene = CreateFactory(ColorGradeSource, EffectStage.ScenePost).Definition;

        Assert.Multiple(() =>
        {
            Assert.That(layer.Stage, Is.EqualTo(EffectStage.Layer));
            Assert.That(scene.Stage, Is.EqualTo(EffectStage.ScenePost));
            Assert.That(layer.Parameters.Select(parameter => parameter.Name), Is.EqualTo(scene.Parameters.Select(parameter => parameter.Name)));
            Assert.That(scene.Parameters.Single(parameter => parameter.Name == "brightness").Maximum, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Texture_runtime_preserves_static_values_for_animatable_parameters_without_a_track()
    {
        var page = new GamePageViewModel(new NoOpGameNavigationService());
        using var resolver = new SkiaShaderEffectProgramResolver(new StaticShaderSource(ColorGradeSource));
        using var runtime = new AvaloniaEffectRuntime(page, new NoOpLayerFactory(), programs: resolver);
        var request = new EffectRequest(
            "",
            "grade",
            "layer",
            0,
            "{\"brightness\":-0.5}",
            "Effects/color-grade.sksl")
        {
            AnimationValues = new Dictionary<string, float> { ["progress"] = 0 }
        };

        await runtime.StartEffectAsync(request, CancellationToken.None);

        var effect = page.TextureEffects.Single();
        Assert.Multiple(() =>
        {
            Assert.That(effect.GetFloat("brightness", 0, -1, 1), Is.EqualTo(-0.5f));
            Assert.That(page.TryGetEffectAnimationValue("grade", "brightness", out var brightness), Is.True);
            Assert.That(brightness, Is.EqualTo(-0.5));
        });
    }

    [Test]
    public async Task Texture_runtime_keeps_blade_count_static_and_binds_only_progress()
    {
        var page = new GamePageViewModel(new NoOpGameNavigationService());
        using var resolver = new SkiaShaderEffectProgramResolver(new StaticShaderSource(BlindsSource));
        using var runtime = new AvaloniaEffectRuntime(page, new NoOpLayerFactory(), programs: resolver);
        var request = new EffectRequest(
            "",
            "blinds",
            "layer",
            0,
            "{\"bladeCount\":14,\"orientation\":\"Vertical\"}",
            "Effects/blinds.sksl")
        {
            AnimationValues = new Dictionary<string, float> { ["progress"] = 0 }
        };

        await runtime.StartEffectAsync(request, CancellationToken.None);

        var effect = page.TextureEffects.Single();
        Assert.Multiple(() =>
        {
            Assert.That(effect.GetFloat("bladeCount", 12, 1, 512), Is.EqualTo(14));
            Assert.That(page.TryGetEffectAnimationValue("blinds", "progress", out _), Is.True);
            Assert.That(page.TryGetEffectAnimationValue("blinds", "bladeCount", out _), Is.False);
        });
    }

    [Test]
    public void Blinds_shader_exposes_only_progress_as_an_animatable_parameter()
    {
        var definition = CreateFactory(BlindsSource, EffectStage.Layer).Definition;

        Assert.That(definition.AnimatableProperties.Select(property => property.Name), Is.EqualTo(["progress"]));
    }

    [Test]
    public void Color_grade_shader_applies_brightness_and_preserves_alpha()
    {
        using var source = Solid(SKColors.Red);
        using var target = new SKBitmap(source.Info);
        var instance = new SceneEffectInstance("grade", CreateFactory(ColorGradeSource, EffectStage.Layer), "layer", 0, "{\"brightness\":-0.5,\"saturation\":0,\"hue\":0}");

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
        var factory = CreateFactory(ColorGradeSource, EffectStage.Layer);
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
        var factory = CreateFactory(BlindsSource, EffectStage.Layer);
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

    [Test]
    public void Blinds_shader_repeats_the_mask_for_each_blade()
    {
        using var source = new SKBitmap(80, 8);
        using var target = new SKBitmap(source.Info);
        using (var canvas = new SKCanvas(source)) canvas.Clear(SKColors.White);
        var instance = new SceneEffectInstance(
            "blinds",
            CreateFactory(BlindsSource, EffectStage.Layer),
            "layer",
            0,
            "{\"bladeCount\":4,\"orientation\":\"Vertical\"}");
        instance.SetAnimatedValue("progress", 0.25f);

        instance.Effect.Render(new SKCanvas(target), source, instance);

        // Every blade reveals its first quarter; the next quarter stays masked.
        Assert.Multiple(() =>
        {
            Assert.That(target.GetPixel(1, 4).Alpha, Is.EqualTo(255));
            Assert.That(target.GetPixel(11, 4).Alpha, Is.EqualTo(0));
            Assert.That(target.GetPixel(21, 4).Alpha, Is.EqualTo(255));
            Assert.That(target.GetPixel(31, 4).Alpha, Is.EqualTo(0));
        });
    }

    [Test]
    public void Blinds_shader_uses_rows_when_orientation_is_horizontal()
    {
        using var source = new SKBitmap(8, 80);
        using var target = new SKBitmap(source.Info);
        using (var canvas = new SKCanvas(source)) canvas.Clear(SKColors.White);
        var instance = new SceneEffectInstance(
            "horizontal-blinds",
            CreateFactory(BlindsSource, EffectStage.Layer),
            "layer",
            0,
            "{\"bladeCount\":4,\"orientation\":\"Horizontal\"}");
        instance.SetAnimatedValue("progress", 0.25f);

        instance.Effect.Render(new SKCanvas(target), source, instance);

        Assert.Multiple(() =>
        {
            Assert.That(target.GetPixel(4, 1).Alpha, Is.EqualTo(255));
            Assert.That(target.GetPixel(4, 11).Alpha, Is.EqualTo(0));
            Assert.That(target.GetPixel(4, 21).Alpha, Is.EqualTo(255));
        });
    }

    [Test]
    public void Project_shader_binds_color_and_vector_metadata_from_json()
    {
        using var source = Solid(SKColors.White);
        using var target = new SKBitmap(source.Info);
        var effect = new SceneEffectInstance(
            "tint",
            CreateFactory(TintSource, EffectStage.Layer),
            "layer",
            0,
            "{\"tint\":\"#00ff00ff\",\"offset\":[0,0]}");

        effect.Effect.Render(new SKCanvas(target), source, effect);
        var result = target.GetPixel(2, 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.Red, Is.LessThan(2));
            Assert.That(result.Green, Is.EqualTo(255));
            Assert.That(result.Blue, Is.LessThan(2));
            Assert.That(result.Alpha, Is.EqualTo(255));
        });
    }

    private static SKBitmap Solid(SKColor color)
    {
        var bitmap = new SKBitmap(8, 8);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        return bitmap;
    }

    private static ShaderProgramTextureEffectFactory CreateFactory(string source, EffectStage stage)
    {
        var resource = new EffectProgramResource("Effects/test.sksl");
        var program = SkiaShaderEffectProgramLoader.Load(resource, source);
        Assert.That(program.IsUsable, Is.True, string.Join(Environment.NewLine, program.Diagnostics));
        return new ShaderProgramTextureEffectFactory("test", stage, resource, program);
    }

    private sealed class StaticShaderSource(string source) : IShaderEffectProgramSource
    {
        public Task<string?> ReadAsync(EffectProgramResource resource, CancellationToken cancellationToken = default) => Task.FromResult<string?>(source);
    }

    private sealed class NoOpLayerFactory : IGamePageLayerFactory
    {
        public SceneTexture ResolveTexture(string assetId) => throw new NotSupportedException();
    }

    private sealed class NoOpGameNavigationService : IGameNavigationService
    {
        public PageViewModelBase? CurrentViewModel => null;
        public bool CanGoBack => false;
        public event EventHandler? CurrentViewModelChanged { add { } remove { } }
        public event EventHandler<GameNavigationChangedEventArgs>? Navigated { add { } remove { } }
        public void Navigate<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade) where TViewModel : PageViewModelBase { }
        public Task NavigateAsync<TViewModel, TArgs>(TArgs args, NavigationTransition transition = NavigationTransition.CrossFade, CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs> => Task.CompletedTask;
        public void ResetTo<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade) where TViewModel : PageViewModelBase { }
        public Task ResetToAsync<TViewModel>(NavigationTransition transition, Func<CancellationToken, Task> loadAsync, CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase => Task.CompletedTask;
        public void GoBack(NavigationTransition transition = NavigationTransition.CrossFade) { }
    }

    private const string ColorGradeSource = """
        /*
        @gal.effect v=1
        @input source
        @targets layer,scenePost
        @param brightness
          uniform: brightness
          type: float
          range: -1..1
        @param saturation
          uniform: saturation
          type: float
          range: -1..1
        @param hue
          uniform: hue
          type: float
          range: -180..180
        */
        uniform shader source;
        uniform float brightness;
        uniform float saturation;
        uniform float hue;
        half4 main(float2 p) {
            half4 c = source.eval(p);
            if (c.a <= 0) return c;
            float3 rgb = clamp(float3(c.rgb) / c.a, 0.0, 1.0);
            float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
            rgb = mix(float3(luma), rgb, max(0.0, 1.0 + saturation));
            float3 axis = float3(0.577350269, 0.577350269, 0.577350269);
            float hueRadians = hue * 0.01745329252;
            rgb = rgb * cos(hueRadians) + cross(axis, rgb) * sin(hueRadians) + axis * dot(axis, rgb) * (1.0 - cos(hueRadians));
            rgb = clamp(rgb + brightness, 0.0, 1.0);
            return half4(half3(rgb * c.a), c.a);
        }
        """;

    private const string BlindsSource = """
        /*
        @gal.effect v=1
        @input source
        @targets layer
        @param progress
          uniform: progress
          type: float
          range: 0..1
          animatable: true
        @param bladeCount
          uniform: bladeCount
          type: float
          step: 1
          animatable: false
        @param orientation
          uniform: horizontal
          type: enum
          options: Vertical,Horizontal
        */
        uniform shader source;
        uniform float progress;
        uniform float bladeCount;
        uniform float horizontal;
        uniform float2 size;
        half4 main(float2 p) {
            float count = max(1.0, floor(bladeCount + 0.5));
            float span = horizontal > 0.5 ? size.y : size.x;
            float coordinate = horizontal > 0.5 ? p.y : p.x;
            float positionInBlade = mod(coordinate, span / count);
            return source.eval(p) * step(positionInBlade, (span / count) * progress);
        }
        """;

    private const string TintSource = """
        /*
        @gal.effect v=1
        @input source
        @targets layer
        @param tint
          uniform: tint
          type: color
        @param offset
          uniform: offset
          type: vec2
        */
        uniform shader source;
        uniform float4 tint;
        uniform float2 offset;
        half4 main(float2 p) { return source.eval(p + offset) * half4(tint); }
        """;
}
