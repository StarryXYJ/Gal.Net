using System.Text.Json.Serialization;

namespace GalNet.Core.Variants;

/// <summary>Author description; directory enumeration belongs to the host, not Core.</summary>
public sealed class ImageVariantsSource
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("folder")]
    public string Folder { get; set; } = "";

    [JsonPropertyName("default")]
    public string Default { get; set; } = "";

    [JsonPropertyName("dimensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ImageVariantDimensionSource>? Dimensions { get; set; }
}

public sealed class ImageVariantDimensionSource
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

/// <summary>A host-enumerated direct child file, including its normal resource identity.</summary>
public sealed record ImageVariantFile(string FileName, string AssetId, string TypeId = "sprite");

public sealed record ImageVariantsDiagnostic(string Code, string Path, string Message);

public sealed class ImageVariantsBuildResult
{
    internal ImageVariantsBuildResult(ImageVariantsDefinition? definition, IEnumerable<ImageVariantsDiagnostic> diagnostics)
    {
        Definition = definition;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public ImageVariantsDefinition? Definition { get; }
    public IReadOnlyList<ImageVariantsDiagnostic> Diagnostics { get; }
    public bool Success => Definition is not null;
}
