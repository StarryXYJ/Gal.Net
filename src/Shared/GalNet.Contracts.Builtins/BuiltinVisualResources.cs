namespace GalNet.Contracts.Builtins;

/// <summary>Stable project identities for the default editable visual assets.</summary>
public static class BuiltinVisualResources
{
    public const string Blur = "b1000000000000000000000000000002";
    public const string Vignette = "b1000000000000000000000000000003";
    public const string ColorGrade = "b1000000000000000000000000000004";
    public const string SpeedLines = "b1000000000000000000000000000006";
    public const string Reveal = "b1000000000000000000000000000007";
    public const string MaskReveal = "b1000000000000000000000000000008";
    public const string ShapeMask = "06bae8b1e0705da98b58fd66b896e109";
    public const string Mood = "4b29dda4fb2b5c46bc3887ba81aaf0ee";
    public static IReadOnlyDictionary<string, string> Programs { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["bloom"] = "b1000000000000000000000000000001",
        ["blur"] = Blur,
        ["vignette"] = Vignette,
        ["colorGrade"] = ColorGrade,
        ["depthBlur"] = "b1000000000000000000000000000005",
        ["speedLines"] = SpeedLines,
        ["reveal"] = Reveal,
        ["maskReveal"] = MaskReveal,
        ["distortion"] = "a498f8b0221355e69ea7bf32b9028400",
        ["radialBlur"] = "17be677c2c2c5795b0ac8f4400065545",
        ["film"] = "25bf866f4ab6578ca6646c62f638fb32",
        ["glitch"] = "c00b818c04ca593493e52fb96598fa30",
        ["pixelate"] = "8a4f9dead06a50deaa7a806e549ab4c6",
        ["spotlight"] = "9ee6516aa64155bdbf9ac66c0e5197b2",
        ["shapeMask"] = ShapeMask,
        ["mood"] = Mood
    });

    public static IReadOnlyDictionary<string, string> WeatherTextures { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Rain"] = "3179deaa20435822a149403dd67e075a",
        ["Snow"] = "e5a8fef331295f738a5eb40f9b5e1148",
        ["Petals"] = "b52e4108949456c8b613c47eec2e4530",
        ["Dust"] = "cd5d35bb40de5941b965d32bd90d08e9",
        ["Fireflies"] = "12d80a90e835574988aec865500726c4"
    });
}
