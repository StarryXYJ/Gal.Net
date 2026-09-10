using GalNet.Core.Scene;

namespace GalNet.Core.View;

public interface ILayerView
{
    void ShowLayer(LayerRenderRequest request);
    void ReplaceLayer(string handleId, string assetId);
    void HideLayer(string handleId);
    void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec);
    Task<AnimationOutcome> AnimateLayerAsync(LayerAnimationRequest request, CancellationToken ct);
    bool SkipLayerAnimationBatch(string? batchId);
}
