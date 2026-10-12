using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinLayerModule : PrimitiveModule
{
    public BuiltinLayerModule(ILayerPresenter? presenter = null) : base(
    [
        BuiltinInstructionFactory.Create<ShowLayerEntry>(context => Show(context, presenter, null)),
        BuiltinInstructionFactory.Create<ShowColorLayerEntry>(context => Show(context, presenter, Arguments.String(context, "color"))),
        BuiltinInstructionFactory.Create<HideLayerEntry>(context => Immediate(context, presenter, target =>
            BuiltinRuntimeActions.HideLayer(context.Runtime, target, Arguments.String(context, "handleId")))),
        BuiltinInstructionFactory.Create<MoveLayerEntry>(context => Immediate(context, presenter, target =>
            BuiltinRuntimeActions.MoveLayer(
                context.Runtime,
                target,
                Arguments.String(context, "handleId"),
                Arguments.Json<LayerTransform>(context, "transform") ?? new LayerTransform(),
                Arguments.Float(context, "z"),
                Arguments.Float(context, "duration")))),
        BuiltinInstructionFactory.Create<ReplaceLayerEntry>(context => Immediate(context, presenter, target =>
            BuiltinRuntimeActions.ReplaceLayer(
                context.Runtime,
                target,
                Arguments.String(context, "handleId"),
                Arguments.String(context, "assetId"))))
    ])
    { }

    private static ImmediatePrimitiveInstance Show(PrimitiveCreateContext context, ILayerPresenter? presenter, string? color) =>
        Immediate(context, presenter, target =>
        {
            var request = new LayerRenderRequest(
                Arguments.String(context, "handleId"),
                Arguments.String(context, "assetId"),
                Arguments.Json<LayerTransform>(context, "transform") ?? new LayerTransform(),
                Arguments.Float(context, "z"),
                Arguments.Enum(context, "displayMode", LayerDisplayMode.Native),
                Arguments.Float(context, "opacity", 1),
                color,
                Arguments.Json<FlipbookDefinition>(context, "flipbook")?.Clone());
            BuiltinRuntimeActions.ShowLayer(context.Runtime, target, request);
        });

    private static ImmediatePrimitiveInstance Immediate(PrimitiveCreateContext context, ILayerPresenter? presenter,
        Action<ILayerPresenter?> action) => new(() => action(presenter), batchId: context.BatchId, catchUp: () => action(null));
}
