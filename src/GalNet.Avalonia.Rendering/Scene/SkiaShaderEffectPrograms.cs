using GalNet.Core.Scene;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>Result of loading one backend-specific shader implementation for a platform-neutral effect resource.</summary>
public sealed record SkiaShaderEffectProgramLoadResult(
    ShaderEffectDescriptor? Descriptor,
    SKRuntimeEffect? RuntimeEffect,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsUsable => Descriptor is not null && RuntimeEffect is not null && Diagnostics.Count == 0;
}

/// <summary>
/// Skia-side loader for an effect program resource. Core parses the resource's annotation contract;
/// this loader compiles the SkSL body and validates the source/parameter binding names exposed by Skia.
/// </summary>
public static class SkiaShaderEffectProgramLoader
{
    public static SkiaShaderEffectProgramLoadResult Load(EffectProgramResource resource, string source)
    {
        var metadata = ShaderEffectMetadataParser.Parse(resource, source);
        var diagnostics = metadata.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToList();
        if (!metadata.Success) return new SkiaShaderEffectProgramLoadResult(null, null, diagnostics);

        var effect = SKRuntimeEffect.CreateShader(source, out var shaderErrors);
        if (effect is null)
        {
            diagnostics.Add($"SkSL compilation failed: {shaderErrors}");
            return new SkiaShaderEffectProgramLoadResult(metadata.Descriptor, null, diagnostics);
        }

        ValidateBindings(effect, metadata.Descriptor!, diagnostics);
        if (diagnostics.Count > 0)
        {
            effect.Dispose();
            return new SkiaShaderEffectProgramLoadResult(metadata.Descriptor, null, diagnostics);
        }
        return new SkiaShaderEffectProgramLoadResult(metadata.Descriptor, effect, diagnostics);
    }

    private static void ValidateBindings(SKRuntimeEffect effect, ShaderEffectDescriptor descriptor, List<string> diagnostics)
    {
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        using var children = new SKRuntimeEffectChildren(effect);
        if (!children.Contains(descriptor.SourceInput))
            diagnostics.Add($"Shader resource '{descriptor.Resource}' must expose source child '{descriptor.SourceInput}'.");

        foreach (var parameter in descriptor.Parameters)
        {
            var exists = parameter.Kind == ShaderEffectParameterKind.Texture
                ? children.Contains(parameter.Uniform)
                : uniforms.Contains(parameter.Uniform);
            if (!exists)
                diagnostics.Add($"Shader resource '{descriptor.Resource}' declares parameter '{parameter.Name}' bound to missing {(parameter.Kind == ShaderEffectParameterKind.Texture ? "child" : "uniform")} '{parameter.Uniform}'.");
        }
    }
}

/// <summary>
/// Built-in Avalonia implementations cached for the process lifetime. Project-provided program loading
/// will use the same <see cref="SkiaShaderEffectProgramLoader"/> in a later phase.
/// </summary>
public sealed class BuiltinSkiaEffectProgramCatalog : IShaderEffectMetadataResolver
{
    public static readonly EffectProgramResource Blinds = new("builtin/blinds");
    public static readonly EffectProgramResource ColorGrade = new("builtin/color-grade");

    private static readonly IReadOnlyDictionary<EffectProgramResource, string> ResourceNames = new Dictionary<EffectProgramResource, string>
    {
        [Blinds] = "GalNet.Avalonia.Rendering.Scene.Effects.Blinds.sksl",
        [ColorGrade] = "GalNet.Avalonia.Rendering.Scene.Effects.ColorGrade.sksl"
    };

    private readonly Dictionary<EffectProgramResource, SkiaShaderEffectProgramLoadResult> _programs;

    public BuiltinSkiaEffectProgramCatalog()
    {
        _programs = ResourceNames.ToDictionary(pair => pair.Key, pair => LoadEmbedded(pair.Key, pair.Value));
    }

    public static BuiltinSkiaEffectProgramCatalog Default { get; } = new();

    public bool TryGetDescriptor(EffectProgramResource resource, out ShaderEffectDescriptor descriptor)
    {
        if (_programs.TryGetValue(resource, out var program) && program.Descriptor is not null)
        {
            descriptor = program.Descriptor;
            return true;
        }
        descriptor = null!;
        return false;
    }

    public bool TryGetProgram(EffectProgramResource resource, out SkiaShaderEffectProgramLoadResult program) =>
        _programs.TryGetValue(resource, out program!);

    public SKRuntimeEffect? GetRuntimeEffect(EffectProgramResource resource) =>
        _programs.TryGetValue(resource, out var program) ? program.RuntimeEffect : null;

    private static SkiaShaderEffectProgramLoadResult LoadEmbedded(EffectProgramResource resource, string resourceName)
    {
        var assembly = typeof(BuiltinSkiaEffectProgramCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return new SkiaShaderEffectProgramLoadResult(null, null, [$"Embedded shader resource '{resourceName}' was not found."]);
        using var reader = new StreamReader(stream);
        return SkiaShaderEffectProgramLoader.Load(resource, reader.ReadToEnd());
    }
}
