using GalNet.Core.Scene;
using GalNet.Core.Assets;
using SkiaSharp;
using System.Diagnostics;

namespace GalNet.Rendering.Scene;

/// <summary>Result of loading one backend-specific shader implementation for a platform-neutral effect resource.</summary>
public sealed record SkiaShaderEffectProgramLoadResult(
    ShaderEffectDescriptor? Descriptor,
    SKRuntimeEffect? RuntimeEffect,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsUsable => Descriptor is not null && RuntimeEffect is not null && Diagnostics.Count == 0;
}

public sealed record ShaderEffectProgramCacheSnapshot(
    int CachedProgramCount,
    long CacheHits,
    long CacheMisses,
    TimeSpan TotalLoadTime);

public sealed record ShaderEffectProgramPreloadResult(
    int RequestedProgramCount,
    int UsableProgramCount,
    int FailedProgramCount,
    ShaderEffectProgramCacheSnapshot Cache);

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

/// <summary>Reads project-owned shader source. The resource key is an asset path, not a built-in name.</summary>
public interface IShaderEffectProgramSource
{
    Task<string?> ReadAsync(EffectProgramResource resource, CancellationToken cancellationToken = default);
}

/// <summary>
/// Caches renderer-specific compiled programs. It has no fallback catalog: a missing project resource
/// produces an unusable result, allowing the caller to retain the unmodified input texture.
/// </summary>
public sealed class SkiaShaderEffectProgramResolver(IShaderEffectProgramSource source) : IShaderEffectMetadataResolver, IDisposable
{
    private readonly IShaderEffectProgramSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Dictionary<EffectProgramResource, SkiaShaderEffectProgramLoadResult> _programs = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _cacheHits;
    private long _cacheMisses;
    private long _totalLoadTicks;
    private int _cachedProgramCount;

    public async Task<SkiaShaderEffectProgramLoadResult> ResolveAsync(EffectProgramResource resource, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_programs.TryGetValue(resource, out var cached))
            {
                Interlocked.Increment(ref _cacheHits);
                return cached;
            }
            Interlocked.Increment(ref _cacheMisses);
            var clock = Stopwatch.StartNew();
            var source = await _source.ReadAsync(resource, cancellationToken);
            var program = source is null
                ? new SkiaShaderEffectProgramLoadResult(null, null, [$"Shader resource '{resource}' was not found."])
                : SkiaShaderEffectProgramLoader.Load(resource, source);
            _programs.Add(resource, program);
            Volatile.Write(ref _cachedProgramCount, _programs.Count);
            clock.Stop();
            Interlocked.Add(ref _totalLoadTicks, clock.Elapsed.Ticks);
            return program;
        }
        finally { _gate.Release(); }
    }

    public async Task<ShaderEffectProgramPreloadResult> PreloadAsync(IEnumerable<EffectProgramResource> resources, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var requested = resources.Distinct().ToArray();
        var usable = 0;
        foreach (var resource in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((await ResolveAsync(resource, cancellationToken)).IsUsable) usable++;
        }
        return new ShaderEffectProgramPreloadResult(requested.Length, usable, requested.Length - usable, Snapshot());
    }

    public ShaderEffectProgramCacheSnapshot Snapshot() => new(
        Volatile.Read(ref _cachedProgramCount),
        Interlocked.Read(ref _cacheHits),
        Interlocked.Read(ref _cacheMisses),
        TimeSpan.FromTicks(Interlocked.Read(ref _totalLoadTicks)));

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

    public void Dispose()
    {
        foreach (var program in _programs.Values) program.RuntimeEffect?.Dispose();
        _programs.Clear();
        Volatile.Write(ref _cachedProgramCount, 0);
        _gate.Dispose();
    }
}

/// <summary>Development/sample source that resolves a project-relative shader path under one asset root.</summary>
public sealed class FileShaderEffectProgramSource(string assetRoot) : IShaderEffectProgramSource
{
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetRoot));

    public async Task<string?> ReadAsync(EffectProgramResource resource, CancellationToken cancellationToken = default)
    {
        if (Path.IsPathRooted(resource.Value)) return null;
        var path = Path.GetFullPath(Path.Combine(_root, resource.Value.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.Equals(Path.GetExtension(path), ".sksl", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path, cancellationToken);
    }
}

/// <summary>Standard project-resource source: program locators are AssetManager GUIDs.</summary>
public sealed class AssetManagerShaderEffectProgramSource(IAssetManager assets) : IShaderEffectProgramSource
{
    private readonly IAssetManager _assets = assets ?? throw new ArgumentNullException(nameof(assets));

    public async Task<string?> ReadAsync(EffectProgramResource resource, CancellationToken cancellationToken = default)
    {
        var source = await _assets.LoadAsync<string>(resource.Value, cancellationToken);
        if (source is null) return null;
        try { return source; }
        finally { _assets.Release<string>(resource.Value); }
    }
}
