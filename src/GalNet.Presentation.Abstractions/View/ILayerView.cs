using GalNet.Core.Scene;

namespace GalNet.Core.View;

/// <summary>Presentation operations for named scene layers.</summary>
public interface ILayerView
{
    /// <summary>Shows a layer using a complete render-state snapshot.</summary>
    void ShowLayer(LayerRenderRequest request);
    /// <summary>Changes the asset displayed by an existing layer without changing its transform.</summary>
    void ReplaceLayer(string handleId, string assetId);
    void HideLayer(string handleId);
    /// <summary>Moves a layer to a new transform and depth over the requested visual duration.</summary>
    void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec);
}
