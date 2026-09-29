using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;

namespace GalNet.Avalonia.GameView.Presentation;

public sealed class AvaloniaLayerPresenter(
    GamePageViewModel state,
    IGamePageLayerFactory layers,
    IAvaloniaUiDispatcher dispatcher) : ILayerPresenter
{
    public void ShowLayer(LayerRenderRequest request) => dispatcher.Dispatch(() =>
        state.SetLayer(request.HandleId, new SceneLayerItem
        {
            HandleId = request.HandleId,
            Texture = request.Color is null ? layers.ResolveTexture(request.AssetId) : null,
            Color = request.Color,
            Flipbook = request.Flipbook?.Clone(),
            X = request.Transform.X,
            Y = request.Transform.Y,
            RotationDegrees = request.Transform.RotationDegrees,
            ScaleX = request.Transform.ScaleX,
            ScaleY = request.Transform.ScaleY,
            Z = request.Z,
            DisplayMode = request.DisplayMode,
            Opacity = request.Opacity
        }));

    public void ReplaceLayer(string handleId, string assetId) =>
        dispatcher.Dispatch(() => state.ReplaceLayer(handleId, layers.ResolveTexture(assetId)));

    public void HideLayer(string handleId) => dispatcher.Dispatch(() => state.HideLayer(handleId));

    public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds) =>
        dispatcher.Dispatch(() => state.MoveLayer(handleId, transform, z));
}
