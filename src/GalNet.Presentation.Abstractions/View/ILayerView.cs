namespace GalNet.Core.View;

public interface ILayerView
{
    void ShowLayer(string id, string assetId, float x, float y, float z = 0);
    void HideLayer(string id);
    void MoveLayer(string id, float x, float y, float z, float durationSec);
}
