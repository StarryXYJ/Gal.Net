using System.Collections.Frozen;

namespace GalNet.Contracts.Builtins.Variants;

public enum ImageVariantResolutionReason
{
    Exact,
    Reduced,
    Default
}

public sealed class ImageVariantCandidate
{
    internal ImageVariantCandidate(IReadOnlyDictionary<string, string> selection, ImageVariant? match)
    {
        Selection = selection.ToFrozenDictionary(StringComparer.Ordinal);
        Match = match;
    }

    public IReadOnlyDictionary<string, string> Selection { get; }
    public ImageVariant? Match { get; }
}

/// <summary>An immutable request and its display result. Fallback never overwrites the request.</summary>
public sealed class ImageVariantResolution
{
    internal ImageVariantResolution(IReadOnlyDictionary<string, string> request, IEnumerable<string> protectedDimensions,
        ImageVariant variant, ImageVariantResolutionReason reason, IEnumerable<string> removedDimensions,
        IEnumerable<ImageVariantCandidate> candidates)
    {
        Request = request.ToFrozenDictionary(StringComparer.Ordinal);
        ProtectedDimensions = Array.AsReadOnly(protectedDimensions.ToArray());
        Variant = variant;
        Reason = reason;
        RemovedDimensions = Array.AsReadOnly(removedDimensions.ToArray());
        Candidates = Array.AsReadOnly(candidates.ToArray());
    }

    public IReadOnlyDictionary<string, string> Request { get; }
    public IReadOnlyList<string> ProtectedDimensions { get; }
    public ImageVariant Variant { get; }
    public ImageVariantResolutionReason Reason { get; }
    public IReadOnlyList<string> RemovedDimensions { get; }
    public IReadOnlyList<ImageVariantCandidate> Candidates { get; }
}

public static class ImageVariantsResolver
{
    public static ImageVariantResolution Initialize(ImageVariantsDefinition definition, IReadOnlyDictionary<string, string>? selection = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var request = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < definition.Default.Values.Count; index++)
            request.Add(definition.Dimensions[index].Key, definition.Default.Values[index]);
        if (selection is not null)
            foreach (var pair in selection) request[pair.Key] = pair.Value;
        return Resolve(definition, request, selection?.Keys ?? []);
    }

    /// <summary>The named dimension is protected even when its value has not changed.</summary>
    public static ImageVariantResolution Select(ImageVariantsDefinition definition, IReadOnlyDictionary<string, string> request,
        string dimension, string value)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(dimension);
        var updated = request.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        updated[dimension] = value;
        return Resolve(definition, updated, [dimension]);
    }

    public static ImageVariantResolution Resolve(ImageVariantsDefinition definition, IReadOnlyDictionary<string, string> request,
        IEnumerable<string> protectedDimensions)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(protectedDimensions);
        var candidate = request.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in candidate)
        {
            var dimension = definition.Dimensions.FirstOrDefault(item => item.Key == pair.Key);
            if (dimension is null || !dimension.Options.Contains(pair.Value, StringComparer.Ordinal))
                throw new ArgumentException($"Unknown dimension or value '{pair.Key}={pair.Value}'.", nameof(request));
        }
        var protectedKeys = protectedDimensions.ToHashSet(StringComparer.Ordinal);
        if (protectedKeys.Any(key => key is null || !candidate.ContainsKey(key)))
            throw new ArgumentException("Protected dimensions must be present in the request.", nameof(protectedDimensions));
        var orderedProtected = definition.Dimensions.Where(item => protectedKeys.Contains(item.Key)).Select(item => item.Key).ToArray();
        var removed = new List<string>();
        var candidates = new List<ImageVariantCandidate>();
        var match = TryMatch();
        if (match is not null) return Result(match, ImageVariantResolutionReason.Exact);
        for (var index = definition.Dimensions.Count - 1; index >= 0; index--)
        {
            var key = definition.Dimensions[index].Key;
            if (protectedKeys.Contains(key) || !candidate.Remove(key)) continue;
            removed.Add(key);
            match = TryMatch();
            if (match is not null) return Result(match, ImageVariantResolutionReason.Reduced);
        }
        return Result(definition.Default, ImageVariantResolutionReason.Default);

        ImageVariant? TryMatch()
        {
            var found = definition.Match(candidate);
            candidates.Add(new ImageVariantCandidate(candidate, found));
            return found;
        }

        ImageVariantResolution Result(ImageVariant variant, ImageVariantResolutionReason reason) =>
            new(request, orderedProtected, variant, reason, removed, candidates);
    }
}
