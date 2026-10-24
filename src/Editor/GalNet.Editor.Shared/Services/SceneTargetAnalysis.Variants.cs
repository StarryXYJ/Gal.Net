using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Variants;

namespace GalNet.Editor.Shared.Services;

/// <summary>Possible atlas identities, separate from the actual sprite and retained selection intent.</summary>
public sealed class SceneImageVariants
{
    internal SceneImageVariants(IEnumerable<string> assetIds, bool definite, ImageVariantResolution? selection)
    {
        AssetIds = Array.AsReadOnly(assetIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
        Definite = definite;
        Selection = selection;
    }

    public IReadOnlyList<string> AssetIds { get; }
    public bool Definite { get; }
    public ImageVariantResolution? Selection { get; }
}

public sealed partial class SceneTargetAnalysis
{
    private void ShowVariants(State state, string handle, string assetId, JsonElement selection)
    {
        if (!state.Targets.TryGetValue(handle, out var target) || target.Kind != SceneTargetKind.Layer) return;
        if (string.IsNullOrWhiteSpace(assetId)) throw new InvalidDataException("Missing image variants resource.");
        assetId = assetId.Trim();
        var binding = new SceneImageVariants([assetId], true, null);
        state.Targets[handle] = target with { Resource = "", Variants = binding };
        var request = VariantsArgumentValidation.ReadSelection(selection);
        if (_variants?.GetValueOrDefault(assetId) is not { } definition) { state.Complete = false; return; }
        var resolved = ImageVariantsResolver.Initialize(definition, request);
        state.Targets[handle] = target with { Resource = resolved.Variant.AssetId, Variants = new([assetId], true, resolved) };
    }

    private void SelectVariants(State state, string handle, string dimension, string value)
    {
        if (!state.Targets.TryGetValue(handle, out var target) || target.Kind != SceneTargetKind.Layer || target.Variants is not { } binding)
        { state.Complete = false; return; }
        // A new selection must not leave the old sprite advertised when the request cannot be resolved.
        state.Targets[handle] = target with { Resource = "", Variants = new(binding.AssetIds, binding.Definite, null) };
        if (!binding.Definite || binding.Selection is null || _variants?.GetValueOrDefault(binding.AssetIds.Single()) is not { } definition)
        { state.Complete = false; return; }
        var resolved = ImageVariantsResolver.Select(definition, binding.Selection.Request, dimension, value);
        state.Targets[handle] = target with { Resource = resolved.Variant.AssetId, Variants = new(binding.AssetIds, true, resolved) };
    }

    private static SceneImageVariants? MergeVariants(IReadOnlyList<SceneTarget> candidates, bool sameKind)
    {
        if (!sameKind) return null;
        var bindings = candidates.Select(target => target.Variants).ToArray();
        var ids = bindings.OfType<SceneImageVariants>().SelectMany(binding => binding.AssetIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0) return null;
        var definite = ids.Length == 1 && bindings.All(binding => binding is { Definite: true });
        var first = bindings[0]?.Selection;
        var selection = definite && bindings.All(binding => EqualSelection(first, binding!.Selection)) ? first : null;
        return new(ids, definite, selection);
    }

    private static bool EqualVariants(SceneImageVariants? left, SceneImageVariants? right) => ReferenceEquals(left, right)
        || left is not null && right is not null && left.Definite == right.Definite
        && left.AssetIds.SequenceEqual(right.AssetIds, StringComparer.OrdinalIgnoreCase) && EqualSelection(left.Selection, right.Selection);

    private static bool EqualSelection(ImageVariantResolution? left, ImageVariantResolution? right) => ReferenceEquals(left, right)
        || left is not null && right is not null && left.Variant.Key == right.Variant.Key && left.Variant.AssetId == right.Variant.AssetId
        && left.Reason == right.Reason && left.ProtectedDimensions.SequenceEqual(right.ProtectedDimensions)
        && left.Request.Count == right.Request.Count && left.Request.All(pair => right.Request.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
