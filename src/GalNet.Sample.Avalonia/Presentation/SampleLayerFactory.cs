using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Rendering.Scene;
using GalNet.Runtime.Logging;

namespace GalNet.Sample.Avalonia.Presentation;

/// <summary>Resolves sample game assets into Avalonia controls for the shared game page.</summary>
internal sealed class SampleLayerFactory(string assetRoot) : IGamePageLayerFactory, IDisposable
{
    private readonly Dictionary<string, SceneTexture> _textures = new(StringComparer.OrdinalIgnoreCase);

    public SceneTexture ResolveTexture(string assetId)
    {
        if (_textures.TryGetValue(assetId, out var texture)) return texture;
        texture = LoadTexture(assetId);
        _textures.Add(assetId, texture);
        return texture;
    }

    private SceneTexture LoadTexture(string assetId)
    {
        var path = Path.IsPathRooted(assetId) ? assetId : Path.Combine(assetRoot, assetId);
        try
        {
            if (File.Exists(path))
                return SceneTexture.FromFile(path);

            ReportMissing(assetId, null);
        }
        catch (Exception exception)
        {
            ReportMissing(assetId, exception);
        }

        return new SceneTexture(LayerImageFallback.MissingImage);
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
    }

    private static void ReportMissing(string assetId, Exception? exception)
    {
        if (!LayerImageFallback.ShouldReport(assetId)) return;
        if (exception is null)
            GameLog.Logger.Warning("Layer asset was not found; rendering built-in fallback: {AssetId}", assetId);
        else
            GameLog.Logger.Warning(exception, "Layer asset could not be read; rendering built-in fallback: {AssetId}", assetId);
    }
}
