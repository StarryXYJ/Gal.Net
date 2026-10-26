using System.Collections.Frozen;

namespace GalNet.Contracts.Builtins.Variants;

/// <summary>One prepared content scope. Definitions are immutable and never resolved through a later scope.</summary>
public sealed class ImageVariantsCatalog
{
    private readonly FrozenDictionary<string, ImageVariantsDefinition> _definitions;
    public static ImageVariantsCatalog Empty { get; } = new([]);

    public ImageVariantsCatalog(IEnumerable<KeyValuePair<string, ImageVariantsDefinition>> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var snapshot = new Dictionary<string, ImageVariantsDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in definitions)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || !snapshot.TryAdd(pair.Key.Trim(), pair.Value))
                throw new InvalidDataException("Image variants catalog requires unique nonempty resource IDs and valid definitions.");
        }
        _definitions = snapshot.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public ImageVariantsDefinition Get(string assetId) => _definitions.TryGetValue(assetId.Trim(), out var definition)
        ? definition : throw new InvalidDataException($"Image variants resource '{assetId}' was not prepared for this content scope.");
}
