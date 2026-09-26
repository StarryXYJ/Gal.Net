using System.Globalization;
using System.Text.Json;
using GalNet.Core.Scene;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>Metadata-driven SkSL texture pass with no concrete effect, Layer, or ScenePost knowledge.</summary>
public sealed class ShaderProgramTextureEffect : IGpuTextureEffect
{
    private readonly ShaderEffectDescriptor _descriptor;
    private readonly SKRuntimeEffect? _effect;

    public ShaderProgramTextureEffect(ShaderEffectDescriptor descriptor, SKRuntimeEffect? effect)
    {
        _descriptor = descriptor;
        _effect = effect;
    }

    public void Render(SKCanvas target, SKBitmap source, SceneEffectInstance instance) => Draw(target, source, source.Width, source.Height, instance);
    public void Render(SKCanvas target, SKImage source, SceneEffectInstance instance) => Draw(target, source, source.Width, source.Height, instance);

    private void Draw(SKCanvas target, SKBitmap source, int width, int height, SceneEffectInstance instance)
    {
        if (_effect is null) { target.DrawBitmap(source, 0, 0); return; }
        using var input = source.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        DrawShader(target, input, width, height, instance);
    }

    private void Draw(SKCanvas target, SKImage source, int width, int height, SceneEffectInstance instance)
    {
        if (_effect is null) { target.DrawImage(source, 0, 0); return; }
        using var input = source.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        DrawShader(target, input, width, height, instance);
    }

    private void DrawShader(SKCanvas target, SKShader input, int width, int height, SceneEffectInstance instance)
    {
        using var uniforms = new SKRuntimeEffectUniforms(_effect!);
        using var children = new SKRuntimeEffectChildren(_effect!);
        if (uniforms.Contains("size")) uniforms.Add("size", new SKSize(width, height));
        children.Add(_descriptor.SourceInput, input);
        foreach (var parameter in _descriptor.Parameters) BindParameter(uniforms, parameter, instance);
        using var shader = _effect!.ToShader(uniforms, children);
        using var paint = new SKPaint { Shader = shader, IsAntialias = false };
        target.DrawRect(0, 0, width, height, paint);
    }

    private static void BindParameter(SKRuntimeEffectUniforms uniforms, ShaderEffectParameterDescriptor parameter, SceneEffectInstance instance)
    {
        if (parameter.Kind == ShaderEffectParameterKind.Texture)
        {
            if (parameter.Required)
                throw new NotSupportedException($"Shader effect texture parameter '{parameter.Name}' requires a texture resolver.");
            return;
        }
        if (!uniforms.Contains(parameter.Uniform)) return;

        switch (parameter.Kind)
        {
            case ShaderEffectParameterKind.Float:
                uniforms.Add(parameter.Uniform, ReadFloat(instance, parameter));
                break;
            case ShaderEffectParameterKind.Integer:
                uniforms.Add(parameter.Uniform, ReadInteger(instance, parameter));
                break;
            case ShaderEffectParameterKind.Boolean:
                uniforms.Add(parameter.Uniform, instance.GetBoolean(parameter.Name, ParseBoolean(parameter.DefaultValue)) ? 1f : 0f);
                break;
            case ShaderEffectParameterKind.Enum:
                uniforms.Add(parameter.Uniform, ReadEnum(instance, parameter));
                break;
            case ShaderEffectParameterKind.Color:
                uniforms.Add(parameter.Uniform, ReadColor(instance, parameter));
                break;
            case ShaderEffectParameterKind.Vector2:
                uniforms.Add(parameter.Uniform, ReadVector(instance, parameter, 2));
                break;
            case ShaderEffectParameterKind.Vector4:
                uniforms.Add(parameter.Uniform, ReadVector(instance, parameter, 4));
                break;
            default:
                throw new InvalidOperationException($"Unsupported shader effect parameter kind '{parameter.Kind}'.");
        }
    }

    private static float ReadFloat(SceneEffectInstance instance, ShaderEffectParameterDescriptor parameter)
    {
        var fallback = ParseFloat(parameter.DefaultValue);
        var minimum = parameter.Minimum is { } min ? (float)min : float.NegativeInfinity;
        var maximum = parameter.Maximum is { } max ? (float)max : float.PositiveInfinity;
        var value = instance.GetFloat(parameter.Name, fallback, minimum, maximum);
        return value;
    }

    private static int ReadInteger(SceneEffectInstance instance, ShaderEffectParameterDescriptor parameter) =>
        (int)MathF.Round(ReadFloat(instance, parameter));

    private static float ReadEnum(SceneEffectInstance instance, ShaderEffectParameterDescriptor parameter)
    {
        var options = parameter.Options ?? [];
        var fallback = parameter.DefaultValue ?? (options.Count > 0 ? options[0] : "");
        var value = instance.GetString(parameter.Name, fallback);
        for (var index = 0; index < options.Count; index++)
            if (string.Equals(options[index], value, StringComparison.Ordinal)) return index;
        return 0;
    }

    private static float[] ReadVector(SceneEffectInstance instance, ShaderEffectParameterDescriptor parameter, int dimension)
    {
        if (instance.TryGetStaticValue(parameter.Name, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            var components = value.EnumerateArray().Select(element => element.TryGetSingle(out var component) ? component : float.NaN).ToArray();
            if (components.Length == dimension && components.All(float.IsFinite)) return components;
        }
        return ParseVector(parameter.DefaultValue, dimension);
    }

    private static float[] ReadColor(SceneEffectInstance instance, ShaderEffectParameterDescriptor parameter)
    {
        if (instance.TryGetStaticValue(parameter.Name, out var value))
        {
            if (value.ValueKind == JsonValueKind.String && TryParseColor(value.GetString(), out var color)) return color;
            if (value.ValueKind == JsonValueKind.Array)
            {
                var components = value.EnumerateArray().Select(element => element.TryGetSingle(out var component) ? component : float.NaN).ToArray();
                if (components.Length == 4 && components.All(float.IsFinite)) return components.Select(component => Math.Clamp(component, 0f, 1f)).ToArray();
            }
        }
        return TryParseColor(parameter.DefaultValue, out var fallback) ? fallback : [0, 0, 0, 0];
    }

    private static float[] ParseVector(string? value, int dimension)
    {
        var components = (value ?? "").Trim().TrimStart('[').TrimEnd(']').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(component => float.TryParse(component, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : float.NaN).ToArray();
        return components.Length == dimension && components.All(float.IsFinite) ? components : new float[dimension];
    }

    private static bool TryParseColor(string? value, out float[] color)
    {
        color = [];
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text[0] != '#') return false;
        var hex = text[1..];
        if (hex.Length is not 6 and not 8 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var packed)) return false;
        var red = hex.Length == 6 ? (packed >> 16) & 0xff : (packed >> 24) & 0xff;
        var green = hex.Length == 6 ? (packed >> 8) & 0xff : (packed >> 16) & 0xff;
        var blue = hex.Length == 6 ? packed & 0xff : (packed >> 8) & 0xff;
        var alpha = hex.Length == 6 ? 0xffu : packed & 0xff;
        color = [red / 255f, green / 255f, blue / 255f, alpha / 255f];
        return true;
    }

    private static float ParseFloat(string? value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f;
    private static bool ParseBoolean(string? value) => bool.TryParse(value, out var parsed) && parsed;
}

/// <summary>Builds a texture-effect factory from a validated shader program resource.</summary>
public class ShaderProgramTextureEffectFactory : ITextureEffectFactory
{
    private readonly SkiaShaderEffectProgramLoadResult _program;

    public ShaderProgramTextureEffectFactory(string effectId, EffectStage stage, EffectProgramResource resource, SkiaShaderEffectProgramLoadResult program)
    {
        _program = program;
        if (!_program.IsUsable)
            throw new InvalidOperationException($"Shader effect program '{resource}' is unavailable: {string.Join(" ", _program?.Diagnostics ?? ["resource not found"])}");
        if (!_program.Descriptor!.Supports(stage))
            throw new InvalidOperationException($"Shader effect program '{resource}' does not support {stage}.");

        Program = resource;
        Definition = new EffectDefinition(
            effectId,
            stage,
            _program.Descriptor.Parameters.Select(ToCatalogParameter).ToArray(),
            _program.Descriptor.Parameters
                .Where(parameter => parameter.Animatable && parameter.Kind is ShaderEffectParameterKind.Float or ShaderEffectParameterKind.Integer)
                .Select(ToAnimatableProperty)
                .ToArray());
    }

    public EffectProgramResource Program { get; }
    public EffectDefinition Definition { get; }
    public ITextureEffect Create() => new ShaderProgramTextureEffect(_program.Descriptor!, _program.RuntimeEffect);

    private static EffectParameterDefinition ToCatalogParameter(ShaderEffectParameterDescriptor parameter) => new(
        parameter.Name,
        parameter.Kind switch
        {
            ShaderEffectParameterKind.Float => EffectParameterKind.Float,
            ShaderEffectParameterKind.Integer => EffectParameterKind.Integer,
            ShaderEffectParameterKind.Boolean => EffectParameterKind.Boolean,
            ShaderEffectParameterKind.Texture => EffectParameterKind.ImageAsset,
            ShaderEffectParameterKind.Enum => EffectParameterKind.Select,
            _ => EffectParameterKind.Text
        },
        parameter.Required,
        parameter.Minimum is { } minimum ? (float)minimum : null,
        parameter.Maximum is { } maximum ? (float)maximum : null,
        parameter.Options);

    private static AnimatableProperty ToAnimatableProperty(ShaderEffectParameterDescriptor parameter) => new(
        parameter.Name,
        AnimationValueKind.Float,
        parameter.Minimum is { } minimum ? (float)minimum : null,
        parameter.Maximum is { } maximum ? (float)maximum : null,
        ParseAnimationDefault(parameter.DefaultValue));

    private static float? ParseAnimationDefault(string? value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && float.IsFinite(parsed)
            ? parsed
            : null;
}
