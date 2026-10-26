using GalNet.Contracts.Builtins.Scene;
using GalNet.Rendering.Scene;

namespace GeneralTest.Scene;

public sealed class DefaultVisualShaderTests
{
    [TestCase("Bloom")]
    [TestCase("Blur")]
    [TestCase("Vignette")]
    [TestCase("ColorGrade")]
    [TestCase("DepthBlur")]
    [TestCase("SpeedLines")]
    [TestCase("Reveal")]
    [TestCase("MaskReveal")]
    public void Default_shader_metadata_and_sksl_compile(string name)
    {
        var result = Load(name);
        using var effect = result.RuntimeEffect;
        Assert.That(result.IsUsable, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }

    internal static SkiaShaderEffectProgramLoadResult Load(string name)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
        var path = Path.Combine(directory.FullName, "src/Editor/GalNet.Editor.Shared/Visuals/Shaders", name + ".sksl");
        return SkiaShaderEffectProgramLoader.Load(new EffectProgramResource(name), File.ReadAllText(path));
    }
}
