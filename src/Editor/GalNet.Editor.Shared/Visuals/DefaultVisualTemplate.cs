using System.Text;
using System.Text.Json;
using System.Collections.ObjectModel;
using GalNet.Contracts.Builtins;
using GalNet.Core.Configuration;

namespace GalNet.Editor.Shared.Visuals;

/// <summary>Initialization source only. Playback loads the deployed project assets.</summary>
public static class DefaultVisualTemplate
{
    private static readonly IReadOnlyDictionary<string, string> Programs = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["bloom"] = "Bloom", ["blur"] = "Blur", ["vignette"] = "Vignette", ["colorGrade"] = "ColorGrade",
        ["depthBlur"] = "DepthBlur", ["speedLines"] = "SpeedLines", ["reveal"] = "Reveal", ["maskReveal"] = "MaskReveal"
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
        var options = result.ReadDefaultOptions();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (purpose, name) in Programs)
        {
            // An explicit author binding, including an empty selection, is never overwritten.
            if (options.DefaultVisualPrograms.ContainsKey(purpose)) continue;
            var id = Guid.NewGuid().ToString("N");
            var path = $"Effects/Default/{name}.sksl";
            files.Add("Assets/" + path, Encoding.UTF8.GetBytes(ReadShader(name)));
            files.Add("Assets/" + path + ".meta", JsonSerializer.SerializeToUtf8Bytes(new { id, type = "effect-program", path }));
            options.DefaultVisualPrograms.Add(purpose, id);
        }
        result.Set(DefaultProjectConfiguration.Section, options);
        return new(result, files);
    }
}

public sealed record DefaultVisualTemplatePlan(ProjectConfiguration Configuration, IReadOnlyDictionary<string, byte[]> Files);
