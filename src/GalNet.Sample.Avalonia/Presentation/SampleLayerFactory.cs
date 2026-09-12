using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Core.Assets;
using GalNet.Rendering.Scene;
using GalNet.Runtime.Logging;

namespace GalNet.Sample.Avalonia.Presentation;

/// <summary>Resolves sample game assets into Avalonia controls for the shared game page.</summary>
internal sealed class SampleLayerFactory(IAssetManager assets) : IGamePageLayerFactory, IDisposable
{
    private readonly IAssetManager _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    private readonly Dictionary<string, SceneTexture> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<SceneTexture> _ownedFallbacks = [];

    public SceneTexture ResolveTexture(string assetId)
    {
        if (_textures.TryGetValue(assetId, out var texture)) return texture;
        texture = LoadTexture(assetId);
        _textures.Add(assetId, texture);
        return texture;
    }

    private SceneTexture LoadTexture(string assetId)
    {
        try
        {
            if (_assets.TryGetLoaded<SceneTexture>(assetId, out var texture))
                return texture;

            ReportMissing(assetId, null);
        }
        catch (Exception exception)
        {
            ReportMissing(assetId, exception);
        }

        var fallback = new SceneTexture(LayerImageFallback.MissingImage);
        _ownedFallbacks.Add(fallback);
        return fallback;
    }

    public void Dispose()
    {
        foreach (var fallback in _ownedFallbacks)
            fallback.Dispose();
        _ownedFallbacks.Clear();
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
