using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinVideoModule : PrimitiveModule
{
    public BuiltinVideoModule(IVideoPresenter? presenter = null) : base(
    [
        PrimitiveImplementation.For<VideoPlayOptions>(BuiltinEntryIds.PlayVideo, (context, options) => CreatePlay(context, presenter, options)),
        PrimitiveImplementation.Immediate<VideoStopOptions>(BuiltinEntryIds.StopVideo, (context, options) =>
            {
                var handleId = options.HandleId ?? "";
                if (context.Runtime.SceneInstances.TryGet<Layer>(handleId, out var layer) && layer.IsVideo)
                    context.Runtime.SceneInstances.Remove<Layer>(handleId, out _);
                presenter?.StopVideo(handleId);
            })
    ])
    { }

    private static VideoPrimitiveInstance CreatePlay(PrimitiveCreateContext context, IVideoPresenter? presenter, VideoPlayOptions options)
    {
        var points = Arguments.Json<double[]>(options.SkipPoints) ?? [];
        var handleId = options.HandleId ?? "";
        var asset = options.Asset ?? "";
        var layer = new LayerRenderRequest(handleId, asset,
            Arguments.Json<LayerTransform>(options.Transform) ?? new LayerTransform(),
            options.Z, options.DisplayMode ?? LayerDisplayMode.Uniform, options.Opacity);
        return new VideoPrimitiveInstance(context.Runtime, presenter,
            new VideoRenderRequest(Guid.NewGuid().ToString("N"), layer, points),
            options.Blocking ?? false, context.BatchId, context.ScopeCancellation);
    }
}
