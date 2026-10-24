using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Core.Variants;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static class LayerVariantsRuntime
{
    public static void ValidateShape(Layer layer)
    {
        if (layer.Variants is not { } state) return;
        if (layer.IsVideo || layer.Color is not null || layer.Flipbook is not null
            || string.IsNullOrWhiteSpace(state.AssetId) || string.IsNullOrEmpty(state.VariantKey) || string.IsNullOrWhiteSpace(layer.AssetId)
            || state.Request is null || state.ProtectedDimensions is null || !Enum.IsDefined(state.Reason)
            || state.Request.Count == 0 || state.Request.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrEmpty(pair.Value))
            || state.ProtectedDimensions.Distinct(StringComparer.Ordinal).Count() != state.ProtectedDimensions.Count
            || state.ProtectedDimensions.Any(key => key is null || !state.Request.ContainsKey(key)))
            throw new InvalidDataException($"Layer '{layer.Id}' has invalid or conflicting image variants state.");
    }

    public static void ValidateContent(Layer layer, ImageVariantsCatalog catalog)
    {
        ValidateShape(layer);
        if (layer.Variants is not { } state) return;
        try
        {
            var result = ImageVariantsResolver.Resolve(catalog.Get(state.AssetId), state.Request, state.ProtectedDimensions);
            if (result.Variant.Key != state.VariantKey || !string.Equals(result.Variant.AssetId, layer.AssetId, StringComparison.OrdinalIgnoreCase) || result.Reason != state.Reason)
                throw new InvalidDataException($"Layer '{layer.Id}' image variants result differs from saved content.");
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException($"Layer '{layer.Id}' image variants selection is incompatible with current content.", error);
        }
    }

    public static void Show(PrimitiveCreateContext context, VariantsShowOptions options, ImageVariantsCatalog catalog, ILayerPresenter? presenter)
    {
        context.ScopeCancellation.ThrowIfCancellationRequested();
        var resolution = ImageVariantsResolver.Initialize(catalog.Get(options.AssetId), VariantsArgumentValidation.ReadSelection(options.Selection));
        var request = new LayerRenderRequest(options.HandleId, resolution.Variant.AssetId,
            VariantsArgumentValidation.ReadTransform(options.Transform), options.Z,
            options.DisplayMode ?? LayerDisplayMode.Native, options.Opacity);
        BuiltinRuntimeActions.ShowLayer(context.Runtime, presenter, request, variants: LayerVariantsState.From(options.AssetId, resolution));
    }

    public static void Select(PrimitiveCreateContext context, VariantsSelectOptions options, ImageVariantsCatalog catalog, ILayerPresenter? presenter)
    {
        context.ScopeCancellation.ThrowIfCancellationRequested();
        string? assetId = null;
        context.Runtime.State.Update(() =>
        {
            context.ScopeCancellation.ThrowIfCancellationRequested();
            if (!context.Runtime.State.Table<Layer>().TryGet(options.HandleId, out var layer) || layer.Variants is not { } state)
                throw new InvalidOperationException($"Layer '{options.HandleId}' is not an active image variants instance.");
            var result = ImageVariantsResolver.Select(catalog.Get(state.AssetId), state.Request, options.Dimension, options.Value);
            var next = LayerVariantsState.From(state.AssetId, result);
            layer.Variants = next;
            layer.AssetId = assetId = result.Variant.AssetId;
        });
        presenter?.ReplaceLayer(options.HandleId, assetId!);
    }
}
