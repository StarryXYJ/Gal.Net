using GalNet.Contracts.Builtins;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinVideoModule(IVideoPresenter? presenter = null) : PrimitiveModule(
    registry =>
    {
        registry.Restore("builtin.videos", context =>
        {
            foreach (var layer in context.Runtime.State.Table<Layer>().Values.Where(value => value.IsVideo))
                context.Start(new VideoPrimitiveInstance(context.Runtime, presenter,
                    new VideoRenderRequest(Guid.NewGuid().ToString("N"), BuiltinLayerModule.Request(layer), []), false, null, context.CancellationToken));
            return Task.CompletedTask;
        }, "builtin.layers");
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
