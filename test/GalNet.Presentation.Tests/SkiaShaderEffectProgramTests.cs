using GalNet.Core.Scene;
using GalNet.Rendering.Scene;

namespace GeneralTest.Scene;

public sealed class SkiaShaderEffectProgramTests
{
    [Test]
    public async Task Project_program_resolves_compiles_and_publishes_metadata()
    {
        var resource = new EffectProgramResource("Effects/test.sksl");
        using var resolver = new SkiaShaderEffectProgramResolver(new StaticSource("""
            /*
            @gal.effect v=1
            @input source
            @targets layer,scenePost
            @param intensity
              uniform: intensity
              type: float
            */
            uniform shader source;
            uniform float intensity;
            half4 main(float2 p) { return source.eval(p) * intensity; }
            """));
        var program = await resolver.ResolveAsync(resource);

        Assert.Multiple(() =>
        {
            Assert.That(program.IsUsable, Is.True, string.Join(Environment.NewLine, program.Diagnostics));
            Assert.That(program.Descriptor, Is.Not.Null);
            Assert.That(program.Descriptor!.Supports(EffectStage.Layer), Is.True);
            Assert.That(program.Descriptor.Supports(EffectStage.ScenePost), Is.True);
            Assert.That(program.Descriptor.Parameters.Select(parameter => parameter.Name), Does.Contain("intensity"));
            Assert.That(resolver.TryGetDescriptor(resource, out var descriptor), Is.True);
            Assert.That(descriptor, Is.SameAs(program.Descriptor));
        });
    }

    [Test]
    public async Task Missing_project_program_has_no_implicit_fallback()
    {
        using var resolver = new SkiaShaderEffectProgramResolver(new StaticSource(null));
        var result = await resolver.ResolveAsync(new EffectProgramResource("Effects/missing.sksl"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsUsable, Is.False);
            Assert.That(result.RuntimeEffect, Is.Null);
            Assert.That(result.Diagnostics, Has.Some.Contains("was not found"));
        });
    }

    [Test]
    public async Task Preload_compiles_each_distinct_program_once_and_reports_cache_hits()
    {
        var source = new CountingSource("""
            /*
            @gal.effect v=1
            @input source
            @targets layer
            */
            uniform shader source;
            half4 main(float2 p) { return source.eval(p); }
            """);
        using var resolver = new SkiaShaderEffectProgramResolver(source);
        var program = new EffectProgramResource("Effects/test.sksl");

        var preload = await resolver.PreloadAsync([program, program]);
        var cached = await resolver.ResolveAsync(program);
        var snapshot = resolver.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(preload.RequestedProgramCount, Is.EqualTo(1));
            Assert.That(preload.UsableProgramCount, Is.EqualTo(1));
            Assert.That(cached.IsUsable, Is.True);
            Assert.That(source.ReadCount, Is.EqualTo(1));
            Assert.That(snapshot.CachedProgramCount, Is.EqualTo(1));
            Assert.That(snapshot.CacheMisses, Is.EqualTo(1));
            Assert.That(snapshot.CacheHits, Is.EqualTo(1));
        });
    }

    [Test]
    public void Loader_rejects_metadata_binding_not_exposed_by_sksl()
    {
        var result = SkiaShaderEffectProgramLoader.Load(new EffectProgramResource("test/missing-binding"), """
            /*
            @gal.effect v=1
            @input source
            @targets layer
            @param intensity
              uniform: uIntensity
              type: float
            */
            uniform shader source;
            half4 main(float2 p) { return source.eval(p); }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsUsable, Is.False);
            Assert.That(result.RuntimeEffect, Is.Null);
            Assert.That(result.Diagnostics, Has.Some.Contains("missing uniform 'uIntensity'"));
        });
    }

    private sealed class StaticSource(string? source) : IShaderEffectProgramSource
    {
        public Task<string?> ReadAsync(EffectProgramResource resource, CancellationToken cancellationToken = default) => Task.FromResult(source);
    }

    private sealed class CountingSource(string source) : IShaderEffectProgramSource
    {
        public int ReadCount { get; private set; }

        public Task<string?> ReadAsync(EffectProgramResource resource, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult<string?>(source);
        }
    }
}
