using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinVideoModule : PrimitiveModule
{
    public BuiltinVideoModule(IVideoPresenter? presenter = null) : base(
    [
        BuiltinInstructionFactory.Create<PlayVideoEntry>(context => CreatePlay(context, presenter)),
        BuiltinInstructionFactory.Create<StopVideoEntry>(context => new ImmediatePrimitiveInstance(() =>
            {
                var handleId = Arguments.String(context, "handleId");
                if (context.Runtime.SceneInstances.TryGet<Layer>(handleId, out var layer) && layer.IsVideo)
                    context.Runtime.SceneInstances.Remove<Layer>(handleId, out _);
                presenter?.StopVideo(handleId);
            }, batchId: context.BatchId))
    ])
    { }

    private static VideoPrimitiveInstance CreatePlay(PrimitiveCreateContext context, IVideoPresenter? presenter)
    {
        var points = Arguments.Json<double[]>(context, "skipPoints") ?? [];
        var handleId = Arguments.String(context, "handleId");
        var asset = Arguments.String(context, "asset");
        var layer = new LayerRenderRequest(handleId, asset,
            Arguments.Json<LayerTransform>(context, "transform") ?? new LayerTransform(),
            Arguments.Float(context, "z"), Arguments.Enum(context, "displayMode", LayerDisplayMode.Uniform),
            Arguments.Float(context, "opacity", 1));
        return new VideoPrimitiveInstance(context.Runtime, presenter,
            new VideoRenderRequest(Guid.NewGuid().ToString("N"), layer, points),
            Arguments.Bool(context, "blocking"), context.BatchId, context.ScopeCancellation);
    }
}
