using GalNet.Core.Scene;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>SkSL source-backed blinds mask. It is intentionally a normal texture pass.</summary>
public sealed class BlindsTextureEffect : IGpuTextureEffect
{
    private static readonly SKRuntimeEffect? Effect = BuiltinSkiaEffectProgramCatalog.Default.GetRuntimeEffect(BuiltinSkiaEffectProgramCatalog.Blinds);

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

    public void Render(SKCanvas target, SKImage source, SceneEffectInstance instance)
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

    internal static void DrawRuntimeShader(SKCanvas target, SKImage source, SKRuntimeEffect? effect, Action<SKRuntimeEffectUniforms> setUniforms)
    {
        if (effect is null) { target.DrawImage(source, 0, 0); return; }
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
public sealed class ColorGradeTextureEffect : IGpuTextureEffect
{
    private static readonly SKRuntimeEffect? Effect = BuiltinSkiaEffectProgramCatalog.Default.GetRuntimeEffect(BuiltinSkiaEffectProgramCatalog.ColorGrade);

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

    public void Render(SKCanvas target, SKImage source, SceneEffectInstance instance)
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
