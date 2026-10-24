using System.Collections.Frozen;

namespace GalNet.Core.Variants;

public sealed class ImageVariantDimension
{
    internal ImageVariantDimension(string key, string label, IEnumerable<string> options)
    {
        Key = key;
        Label = label;
        Options = Array.AsReadOnly(options.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    public string Key { get; }
    public string Label { get; }
    public IReadOnlyList<string> Options { get; }
}

public sealed class ImageVariant
{
    internal ImageVariant(string key, string assetId, IEnumerable<string> values)
    {
        Key = key;
        AssetId = assetId;
        Values = Array.AsReadOnly(values.ToArray());
    }

    public string Key { get; }
    public string AssetId { get; }
    public IReadOnlyList<string> Values { get; }
}

/// <summary>Validated immutable facts, independent of source paths and directory enumeration.</summary>
public sealed class ImageVariantsDefinition
{
    private readonly FrozenDictionary<string, ImageVariant> _variants;

    internal ImageVariantsDefinition(IEnumerable<ImageVariantDimension> dimensions, IEnumerable<ImageVariant> variants, string defaultKey)
    {
        Dimensions = Array.AsReadOnly(dimensions.ToArray());
        Variants = Array.AsReadOnly(variants.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray());
        _variants = Variants.ToFrozenDictionary(item => item.Key, StringComparer.Ordinal);
        Default = _variants[defaultKey];
    }

    public IReadOnlyList<ImageVariantDimension> Dimensions { get; }
    public IReadOnlyList<ImageVariant> Variants { get; }
    public ImageVariant Default { get; }

    internal ImageVariant? Match(IReadOnlyDictionary<string, string> selection)
    {
        // A file name describes a positional prefix. Missing middle positions are not compacted.
        if (selection.Count == 0) return null;
        var values = new string[selection.Count];
        for (var index = 0; index < values.Length; index++)
        {
            if (!selection.TryGetValue(Dimensions[index].Key, out var value)) return null;
            values[index] = value;
        }
        return _variants.GetValueOrDefault(string.Join('_', values));
    }
}
