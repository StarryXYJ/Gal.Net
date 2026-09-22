using GalNet.Core.Scene;

namespace GalNet.Core.View;

public interface ILayerPresenter
{
    void ShowLayer(LayerRenderRequest request);
    void ReplaceLayer(string handleId, string assetId);
    void HideLayer(string handleId);
    void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds);
}
