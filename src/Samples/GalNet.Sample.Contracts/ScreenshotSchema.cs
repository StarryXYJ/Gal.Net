using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;

namespace GalNet.Project.Contracts;

/// <summary>Pure screenshot contract. It knows neither Avalonia nor the host's output directory.</summary>
public static class ScreenshotSchema
{
    public const string TypeId = "sample.screenshot";
    public static IEntryModule CreateModule() => new DefaultEntryModule("sample",
        [new DefaultPrimitiveEntryBase(TypeId, new DynamicParameterTable(
            [new("fileName", typeof(string), constraints: JsonSerializer.SerializeToElement(new { format = "png-file-name", maxLength = 120 }))],
            arguments => { if (arguments.TryGetProperty("fileName", out var value)) ValidateFileName(value.GetString()); }))]);

    public static void ValidateFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)) ||
            name.Trim() != name || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("fileName must be one portable PNG filename, at most 120 characters, without path components.");
        var stem = name.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && stem[3] is >= '1' and <= '9' && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)))
            throw new ArgumentException("fileName must not use a reserved device name.");
    }
}
