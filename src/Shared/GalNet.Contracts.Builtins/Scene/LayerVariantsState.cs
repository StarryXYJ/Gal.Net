using GalNet.Contracts.Builtins.Variants;

namespace GalNet.Contracts.Builtins.Scene;

/// <summary>Persistent selection intent; Layer.AssetId is the actual image.</summary>
public sealed class LayerVariantsState
{
    public string AssetId { get; set; } = "";
    public Dictionary<string, string> Request { get; set; } = new(StringComparer.Ordinal);
    public List<string> ProtectedDimensions { get; set; } = [];
    public string VariantKey { get; set; } = "";
    public ImageVariantResolutionReason Reason { get; set; }

    public static LayerVariantsState From(string assetId, ImageVariantResolution resolution) => new()
    {
        AssetId = assetId,
        Request = resolution.Request.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        ProtectedDimensions = resolution.ProtectedDimensions.ToList(),
        VariantKey = resolution.Variant.Key,
        Reason = resolution.Reason
    };
}
