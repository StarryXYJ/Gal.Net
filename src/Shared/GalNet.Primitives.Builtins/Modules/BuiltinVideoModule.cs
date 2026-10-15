using GalNet.Contracts.Builtins;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinVideoModule(IVideoPresenter? presenter = null) : PrimitiveModule(
    registry =>
    {
        registry.Register<VideoPlayOptions>(BuiltinEntryIds.PlayVideo, (context, options) => new VideoPrimitiveInstance(presenter, context, options));
        registry.Immediate<VideoStopOptions>(BuiltinEntryIds.StopVideo, (context, options) =>
            {
                var handleId = options.HandleId ?? "";
                if (context.Runtime.SceneInstances.TryGet<Layer>(handleId, out var layer) && layer.IsVideo)
                    context.Runtime.SceneInstances.Remove<Layer>(handleId, out _);
                presenter?.StopVideo(handleId);
            });
    })
{

}
