using GalNet.Core.Scene;
using GalNet.Rendering.Scene;

namespace GeneralTest.Scene;

public sealed class SkiaShaderEffectProgramTests
{
    [TestCase("builtin/blinds", "progress", EffectStage.Layer)]
    [TestCase("builtin/color-grade", "hue", EffectStage.ScenePost)]
    public void Builtin_programs_load_compile_and_publish_metadata(string resourceId, string parameterName, EffectStage stage)
    {
        var resource = new EffectProgramResource(resourceId);
        var catalog = BuiltinSkiaEffectProgramCatalog.Default;

        Assert.That(catalog.TryGetProgram(resource, out var program), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(program.IsUsable, Is.True, string.Join(Environment.NewLine, program.Diagnostics));
            Assert.That(program.Descriptor, Is.Not.Null);
            Assert.That(program.Descriptor!.Supports(stage), Is.True);
            Assert.That(program.Descriptor.Parameters.Select(parameter => parameter.Name), Does.Contain(parameterName));
            Assert.That(catalog.TryGetDescriptor(resource, out var descriptor), Is.True);
            Assert.That(descriptor, Is.SameAs(program.Descriptor));
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
}
