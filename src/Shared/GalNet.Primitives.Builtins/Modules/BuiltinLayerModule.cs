using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Variants;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinLayerModule(ILayerPresenter? presenter = null, ImageVariantsCatalog? imageVariants = null) : PrimitiveModule(
    registry =>
    {
        var variants = imageVariants ?? ImageVariantsCatalog.Empty;
        registry.ValidateState((_, state) =>
        {
            foreach (var layer in state.Table<Layer>().Values) LayerVariantsRuntime.ValidateContent(layer, variants);
        });
        registry.State<Layer>("builtin.layers", value => value.Id, context =>
        {
            foreach (var layer in context.Runtime.State.Table<Layer>().Values) LayerVariantsRuntime.ValidateContent(layer, variants);
            if (presenter is null) return Task.CompletedTask;
            foreach (var layer in context.Runtime.State.Table<Layer>().Values.Where(value => !value.IsVideo && value.Visible))
            {
                context.Own(() => { presenter.HideLayer(layer.Id); return Task.CompletedTask; });
                presenter.ShowLayer(Request(layer));
            }
            return Task.CompletedTask;
        }, validate: LayerVariantsRuntime.ValidateShape, version: 2, keySpace: BuiltinScene.KeySpace);
        registry.Register<VariantsShowOptions>(BuiltinEntryIds.ShowVariants, (context, options) => Immediate(context, presenter,
            target => LayerVariantsRuntime.Show(context, options, variants, target)));
        registry.Register<VariantsSelectOptions>(BuiltinEntryIds.SelectVariants, (context, options) => Immediate(context, presenter,
            target => LayerVariantsRuntime.Select(context, options, variants, target)));
        registry.Register<LayerShowOptions>(BuiltinEntryIds.ShowLayer, (context, options) => Immediate(context, presenter, target =>
            BuiltinRuntimeActions.ShowLayer(context.Runtime, target, new LayerRenderRequest(
                options.HandleId ?? "", options.AssetId ?? "", Arguments.Json<LayerTransform>(options.Transform) ?? new LayerTransform(),
                options.Z, options.DisplayMode ?? LayerDisplayMode.Native, options.Opacity,
                Flipbook: Arguments.Json<FlipbookDefinition>(options.Flipbook)?.Clone()))));
        registry.Register<LayerColorOptions>(BuiltinEntryIds.ShowColorLayer, (context, options) => Immediate(context, presenter, target =>
            BuiltinRuntimeActions.ShowLayer(context.Runtime, target, new LayerRenderRequest(
                options.HandleId ?? "", "", Arguments.Json<LayerTransform>(options.Transform) ?? new LayerTransform(),
                options.Z, LayerDisplayMode.Native, options.Opacity, options.Color ?? ""))));
        registry.Register<LayerHandleOptions>(BuiltinEntryIds.HideLayer, (context, options) => Immediate(context, presenter, target =>
            BuiltinRuntimeActions.HideLayer(context.Runtime, target, options.HandleId ?? "")));
        registry.Register<LayerMoveOptions>(BuiltinEntryIds.MoveLayer, (context, options) => Immediate(context, presenter, target =>
            BuiltinRuntimeActions.MoveLayer(
                context.Runtime,
                target,
                options.HandleId ?? "",
                Arguments.Json<LayerTransform>(options.Transform) ?? new LayerTransform(),
                options.Z,
                options.Duration)));
        registry.Register<LayerReplaceOptions>(BuiltinEntryIds.ReplaceLayer, (context, options) => Immediate(context, presenter, target =>
            BuiltinRuntimeActions.ReplaceLayer(
                context.Runtime,
                target,
                options.HandleId ?? "",
                options.AssetId ?? "")));
    })
{
    internal static LayerRenderRequest Request(Layer layer) => new(layer.Id, layer.AssetId, layer.Transform.Clone(), layer.Z,
        layer.DisplayMode, layer.Opacity, layer.Color, layer.Flipbook?.Clone());

    private static ImmediatePrimitiveInstance Immediate(PrimitiveCreateContext context, ILayerPresenter? presenter,
        Action<ILayerPresenter?> action) => new(() => action(presenter), batchId: context.BatchId, catchUp: () => action(null));
}
