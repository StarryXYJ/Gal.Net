using GalNet.Core.Scene;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>SkSL source-backed blinds mask. It is intentionally a normal texture pass.</summary>
public sealed class BlindsTextureEffect : ITextureEffect
{
    private const string Source = """
uniform shader source;
uniform float progress;
uniform float bladeCount;
uniform float horizontal;
uniform float2 size;
half4 main(float2 p) {
    float axis = horizontal > 0.5 ? p.y / size.y : p.x / size.x;
    float reveal = step(fract(axis * bladeCount), progress);
    return source.eval(p) * reveal;
}
""";
    private static readonly SKRuntimeEffect? Effect = CreateEffect(Source);

    public void Render(SKCanvas target, SKBitmap source, SceneEffectInstance instance)
    {
        var progress = instance.GetFloat("progress", 0, 0, 1);
        var blades = instance.GetFloat("bladeCount", 12, 1, 512);
        var horizontal = string.Equals(instance.GetString("orientation", "Vertical"), "Horizontal", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        DrawRuntimeShader(target, source, Effect, uniforms =>
        {
            uniforms.Add("progress", progress);
            uniforms.Add("bladeCount", blades);
            uniforms.Add("horizontal", horizontal);
            uniforms.Add("size", new SKSize(source.Width, source.Height));
        });
    }

    internal static SKRuntimeEffect? CreateEffect(string source)
    {
        var effect = SKRuntimeEffect.CreateShader(source, out var errors);
        if (effect is null) System.Diagnostics.Trace.TraceWarning("Skia effect compilation failed: {0}", errors);
        return effect;
    }

    internal static void DrawRuntimeShader(SKCanvas target, SKBitmap source, SKRuntimeEffect? effect, Action<SKRuntimeEffectUniforms> setUniforms)
    {
        if (effect is null) { target.DrawBitmap(source, 0, 0); return; }
        using var input = source.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        using var children = new SKRuntimeEffectChildren(effect);
        if (uniforms.Contains("size")) uniforms.Add("size", new SKSize(source.Width, source.Height));
        setUniforms(uniforms);
        children.Add("source", input);
        using var shader = effect.ToShader(uniforms, children);
        using var paint = new SKPaint { Shader = shader, IsAntialias = false };
        target.DrawRect(0, 0, source.Width, source.Height, paint);
    }
}

/// <summary>One shared shader implementation used for both Layer and ScenePost grading.</summary>
public sealed class ColorGradeTextureEffect : ITextureEffect
{
    private const string Source = """
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
    rgb = rgb * cos(hue) + cross(axis, rgb) * sin(hue) + axis * dot(axis, rgb) * (1.0 - cos(hue));
    rgb = clamp(rgb + brightness, 0.0, 1.0);
    return half4(half3(rgb * c.a), c.a);
}
""";
    private static readonly SKRuntimeEffect? Effect = BlindsTextureEffect.CreateEffect(Source);

    public void Render(SKCanvas target, SKBitmap source, SceneEffectInstance instance)
    {
        var brightness = instance.GetFloat("brightness", 0, -1, 1);
        var saturation = instance.GetFloat("saturation", 0, -1, 1);
        var hue = instance.GetFloat("hue", 0, -180, 180) * MathF.PI / 180f;
        BlindsTextureEffect.DrawRuntimeShader(target, source, Effect, uniforms =>
        {
            uniforms.Add("brightness", brightness);
            uniforms.Add("saturation", saturation);
            uniforms.Add("hue", hue);
        });
    }
}

public sealed class BlindsTextureEffectFactory : ITextureEffectFactory
{
    public EffectDefinition Definition { get; } = new(
        "mask.blinds", EffectStage.Layer,
        [new("bladeCount", EffectParameterKind.Integer, Minimum: 1), new("orientation", EffectParameterKind.Select, Options: ["Vertical", "Horizontal"])],
        [new("progress", AnimationValueKind.Float, 0, 1)]);
    public ITextureEffect Create() => new BlindsTextureEffect();
}

public sealed class LayerColorGradeTextureEffectFactory : ITextureEffectFactory
{
    public EffectDefinition Definition { get; } = ColorGradeDefinitions.Create("layer.colorGrade", EffectStage.Layer);
    public ITextureEffect Create() => new ColorGradeTextureEffect();
}

public sealed class SceneColorGradeTextureEffectFactory : ITextureEffectFactory
{
    public EffectDefinition Definition { get; } = ColorGradeDefinitions.Create("scene.colorGrade", EffectStage.ScenePost);
    public ITextureEffect Create() => new ColorGradeTextureEffect();
}

internal static class ColorGradeDefinitions
{
    public static EffectDefinition Create(string id, EffectStage stage) => new(id, stage,
        [new("brightness", EffectParameterKind.Float, Minimum: -1, Maximum: 1), new("saturation", EffectParameterKind.Float, Minimum: -1, Maximum: 1), new("hue", EffectParameterKind.Float, Minimum: -180, Maximum: 180)],
        [new("brightness", AnimationValueKind.Float, -1, 1), new("saturation", AnimationValueKind.Float, -1, 1), new("hue", AnimationValueKind.Float, -180, 180)]);
}
