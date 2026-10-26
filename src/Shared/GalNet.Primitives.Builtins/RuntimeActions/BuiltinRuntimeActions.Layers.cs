using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    public static void ShowLayer(IGameContext runtime, ILayerPresenter? presenter, LayerRenderRequest request, bool notify = true,
        LayerVariantsState? variants = null)
    {
        runtime.State.Update(() =>
        {
            runtime.SceneInstances.Remove<Layer>(request.HandleId, out _);
            runtime.SceneInstances.GetOrAdd(request.HandleId, id => new Layer
            {
                Id = id,
                AssetId = request.AssetId,
                Color = request.Color,
                Flipbook = request.Flipbook?.Clone(),
                Variants = variants,
                Transform = request.Transform.Clone(),
                Z = request.Z,
                Opacity = request.Opacity,
                DisplayMode = request.DisplayMode,
                Visible = true
            });
        });
        if (notify)
            presenter?.ShowLayer(request);
    }

    public static void HideLayer(IGameContext runtime, ILayerPresenter? presenter, string handleId, bool notify = true)
    {
        runtime.SceneInstances.Remove<Layer>(handleId, out _);
        if (notify)
            presenter?.HideLayer(handleId);
    }

    public static void MoveLayer(
        IGameContext runtime,
        ILayerPresenter? presenter,
        string handleId,
        LayerTransform transform,
        float z,
        float durationSeconds,
        bool notify = true)
    {
        runtime.State.Update(() =>
        {
            if (runtime.SceneInstances.TryGet<Layer>(handleId, out var layer))
            {
                layer.Transform = transform.Clone();
                layer.Z = z;
            }
        });
        if (notify)
            presenter?.MoveLayer(handleId, transform, z, durationSeconds);
    }

    public static void ReplaceLayer(IGameContext runtime, ILayerPresenter? presenter, string handleId, string assetId, bool notify = true)
    {
        runtime.State.Update(() =>
        {
            if (runtime.SceneInstances.TryGet<Layer>(handleId, out var layer))
            {
                runtime.State.Table<Layer>().Set(new Layer
                {
                    Id = handleId,
                    AssetId = assetId,
                    Transform = layer.Transform.Clone(),
                    Z = layer.Z,
                    Opacity = layer.Opacity,
                    DisplayMode = layer.DisplayMode,
                    Visible = layer.Visible,
                    Flipbook = layer.Flipbook?.Clone()
                });
            }
        });
        if (notify)
            presenter?.ReplaceLayer(handleId, assetId);
    }
}
