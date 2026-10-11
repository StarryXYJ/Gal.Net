using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    public static void ShowLayer(IGameRuntime runtime, ILayerPresenter? presenter, LayerRenderRequest request, bool notify = true)
    {
        var layer = runtime.SceneInstances.GetOrAdd(request.HandleId, id => new Layer { Id = id });
        layer.AssetId = request.AssetId;
        layer.IsVideo = false;
        layer.VideoPlaybackId = null;
        layer.Color = request.Color;
        layer.Flipbook = request.Flipbook?.Clone();
        layer.Transform = request.Transform.Clone();
        layer.Z = request.Z;
        layer.Opacity = request.Opacity;
        layer.DisplayMode = request.DisplayMode;
        layer.Visible = true;
        if (notify)
            presenter?.ShowLayer(request);
    }

    public static void HideLayer(IGameRuntime runtime, ILayerPresenter? presenter, string handleId, bool notify = true)
    {
        runtime.SceneInstances.Remove<Layer>(handleId, out _);
        if (notify)
            presenter?.HideLayer(handleId);
    }

    public static void MoveLayer(
        IGameRuntime runtime,
        ILayerPresenter? presenter,
        string handleId,
        LayerTransform transform,
        float z,
        float durationSeconds,
        bool notify = true)
    {
        if (runtime.SceneInstances.TryGet<Layer>(handleId, out var layer))
        {
            layer.Transform = transform.Clone();
            layer.Z = z;
        }
        if (notify)
            presenter?.MoveLayer(handleId, transform, z, durationSeconds);
    }

    public static void ReplaceLayer(IGameRuntime runtime, ILayerPresenter? presenter, string handleId, string assetId, bool notify = true)
    {
        if (runtime.SceneInstances.TryGet<Layer>(handleId, out var layer))
        {
            layer.AssetId = assetId;
            layer.IsVideo = false;
            layer.VideoPlaybackId = null;
            layer.Color = null;
        }
        if (notify)
            presenter?.ReplaceLayer(handleId, assetId);
    }
}
