using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GalNet.Control.Screen.Game;
using Serilog;
using AvaloniaImage = Avalonia.Controls.Image;
using GalNet.Core.Assets;
using GalNet.Core.Scene;
using GalNet.Core.View;

namespace GalNet.Control.Runtime.Presentation;

internal sealed class DefaultGameViewRegistry
{
    private readonly GameScreenView _gameScreen;
    private readonly IAssetManager? _assets;
    private readonly Dictionary<string, AvaloniaImage> _layers = new(StringComparer.OrdinalIgnoreCase);

    public DefaultGameViewRegistry(GameScreenView gameScreen, IAssetManager? assets)
    {
        _gameScreen = gameScreen;
        _assets = assets;
    }

    public void ShowLayer(LayerRenderRequest request)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_layers.TryGetValue(request.HandleId, out var existing))
            {
                ApplyTransform(existing, request.Transform, request.Z);
                _ = LoadLayerSourceAsync(request.HandleId, existing, request.AssetId);
                return;
            }

            var img = new AvaloniaImage { Opacity = 1, Stretch = Avalonia.Media.Stretch.Uniform };
            ApplyTransform(img, request.Transform, request.Z);

            _layers[request.HandleId] = img;
            _gameScreen.LayerCanvas.Children.Add(img);
            _ = LoadLayerSourceAsync(request.HandleId, img, request.AssetId);
        });
    }

    public void ReplaceLayer(string handleId, string assetId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_layers.TryGetValue(handleId, out var image)) _ = LoadLayerSourceAsync(handleId, image, assetId);
        });
    }

    private async Task LoadLayerSourceAsync(string layerId, AvaloniaImage image, string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return;
        try
        {
            Bitmap bitmap;
            var file = _assets is null ? null : await _assets.GetFileAsync(assetId);
            if (file is not null)
            {
                var bytes = await file.ReadAllBytesAsync();
                using var stream = new MemoryStream(bytes);
                bitmap = new Bitmap(stream);
            }
            else if (File.Exists(assetId))
            {
                bitmap = new Bitmap(assetId);
            }
            else
            {
                Log.Warning("Layer image asset was not found: {AssetId}", assetId);
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_layers.TryGetValue(layerId, out var current) && ReferenceEquals(current, image))
                {
                    if (current.Source is Bitmap previous) previous.Dispose();
                    current.Source = bitmap;
                }
                else
                    bitmap.Dispose();
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load layer image asset: {AssetId}", assetId);
        }
    }

    public void HideLayer(string id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_layers.Remove(id, out var img))
            {
                _gameScreen.LayerCanvas.Children.Remove(img);
                if (img.Source is Bitmap bitmap) bitmap.Dispose();
            }
        });
    }

    public void MoveLayer(string id, LayerTransform transform, float z, float durationSec)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_layers.TryGetValue(id, out var img))
                return;

            ApplyTransform(img, transform, z);
        });
    }

    private static void ApplyTransform(AvaloniaImage image, LayerTransform transform, float z)
    {
        image.SetValue(Avalonia.Controls.Canvas.LeftProperty, (double)transform.X);
        image.SetValue(Avalonia.Controls.Canvas.TopProperty, (double)transform.Y);
        image.SetValue(Avalonia.Controls.Canvas.ZIndexProperty, (int)z);
        image.RenderTransformOrigin = Avalonia.RelativePoint.Center;
        image.RenderTransform = new Avalonia.Media.TransformGroup
        {
            Children =
            [
                new Avalonia.Media.ScaleTransform(transform.ScaleX, transform.ScaleY),
                new Avalonia.Media.RotateTransform(transform.RotationDegrees)
            ]
        };
    }

}
