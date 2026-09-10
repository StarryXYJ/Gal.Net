using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Runtime.Logging;

namespace GalNet.Sample.Avalonia.Presentation;

/// <summary>Resolves sample game assets into Avalonia controls for the shared game page.</summary>
internal sealed class SampleLayerFactory(string assetRoot) : IGamePageLayerFactory
{
    public IImage? ResolveLayerImage(string assetId)
    {
        var path = Path.IsPathRooted(assetId) ? assetId : Path.Combine(assetRoot, assetId);
        try
        {
            if (File.Exists(path))
                return new Bitmap(path);

            GameLog.Logger.Warning("Layer asset was not found; rendering placeholder: {AssetId}", assetId);
        }
        catch (Exception exception)
        {
            GameLog.Logger.Warning(exception, "Layer asset could not be read; rendering placeholder: {AssetId}", assetId);
        }

        return null;
    }
}
