using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinLayerModule(ILayerPresenter? presenter = null) : PrimitiveModule(
    registry =>
    {
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
    private static ImmediatePrimitiveInstance Immediate(PrimitiveCreateContext context, ILayerPresenter? presenter,
        Action<ILayerPresenter?> action) => new(() => action(presenter), batchId: context.BatchId, catchUp: () => action(null));
}
