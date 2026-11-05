using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Configuration;

namespace GalNet.Editor.Shared.Visuals;

/// <summary>Initialization source only. Playback loads the deployed project assets.</summary>
public static class DefaultVisualTemplate
{
    private static readonly IReadOnlyDictionary<string, string> Programs = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["bloom"] = "Bloom",
        ["blur"] = "Blur",
        ["vignette"] = "Vignette",
        ["colorGrade"] = "ColorGrade",
        ["depthBlur"] = "DepthBlur",
        ["speedLines"] = "SpeedLines",
        ["reveal"] = "Reveal",
        ["maskReveal"] = "MaskReveal",
        ["distortion"] = "Distortion",
        ["radialBlur"] = "RadialBlur",
        ["film"] = "Film",
        ["glitch"] = "Glitch",
        ["pixelate"] = "Pixelate",
        ["spotlight"] = "Spotlight",
        ["shapeMask"] = "ShapeMask",
        ["mood"] = "Mood"
    });

    public static IReadOnlyDictionary<string, string> ProgramNames => Programs;

    public static string ReadShader(string name)
    {
        if (!Programs.Values.Contains(name, StringComparer.Ordinal)) throw new ArgumentException("Unknown template shader.", nameof(name));
        using var stream = typeof(DefaultVisualTemplate).Assembly.GetManifestResourceStream($"GalNet.Visuals.{name}.sksl")
            ?? throw new InvalidOperationException($"Missing visual template '{name}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Builds an additive plan without writing or changing the caller's configuration.</summary>
    public static DefaultVisualTemplatePlan Prepare(ProjectConfiguration configuration)
    {
        var result = configuration.Clone();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (purpose, name) in Programs)
        {
            var id = BuiltinVisualResources.Programs[purpose];
            var path = $"Effects/Default/{name}.sksl";
            files.Add("Buildin/" + path, Encoding.UTF8.GetBytes(ReadShader(name)));
            files.Add("Buildin/" + path + ".meta", JsonSerializer.SerializeToUtf8Bytes(new { id, type = "effect-program", path }));
        }
        foreach (var (purpose, name) in new Dictionary<string, string> { ["Rain"] = "Rain", ["Snow"] = "Soft", ["Petals"] = "Petal", ["Dust"] = "Soft", ["Fireflies"] = "Soft" })
        {
            var id = BuiltinVisualResources.WeatherTextures[purpose]; var path = "Effects/Default/" + purpose + "Particle.png";
            using var stream = typeof(DefaultVisualTemplate).Assembly.GetManifestResourceStream("GalNet.Visuals.Particles." + name + ".png")
                ?? throw new InvalidOperationException("Missing weather texture.");
            using var memory = new MemoryStream(); stream.CopyTo(memory);
            files.Add("Buildin/" + path, memory.ToArray());
            files.Add("Buildin/" + path + ".meta", JsonSerializer.SerializeToUtf8Bytes(new { id, type = "sprite", path }));
        }
        files.Add(DefaultParticlePresets.Path, DefaultParticlePresets.Create());
        files.Add(DefaultParticlePresets.Path + ".meta", JsonSerializer.SerializeToUtf8Bytes(new { id = DefaultParticlePresets.AssetId, type = "data", path = "Presets/entries.json" }));
        return new(result, files);
    }
}

public sealed record DefaultVisualTemplatePlan(ProjectConfiguration Configuration, IReadOnlyDictionary<string, byte[]> Files);
