using GalNet.Core.Scene;

namespace GalNet.Core.View;

/// <summary>Complete render state for one resource-backed scene layer instance.</summary>
public sealed record LayerRenderRequest(
    string HandleId,
    string AssetId,
    LayerTransform Transform,
    float Z,
    LayerDisplayMode DisplayMode,
    float Opacity);
