using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GalNet.Control.Screen.BuiltIn;
using Serilog;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaImage = Avalonia.Controls.Image;
using GalNet.Core.Assets;

namespace GalNet.Control.View;

internal sealed class DefaultGameViewRegistry
{
    private readonly GameScreenView _gameScreen;
    private readonly IAssetManager? _assets;
    private readonly Dictionary<string, AvaloniaImage> _layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AvaloniaControl> _widgets = new(StringComparer.OrdinalIgnoreCase);

    public DefaultGameViewRegistry(GameScreenView gameScreen, IAssetManager? assets)
    {
        _gameScreen = gameScreen;
        _assets = assets;
    }

    public void ShowLayer(string id, string assetId, float x, float y, float z = 0)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_layers.TryGetValue(id, out var existing))
            {
                existing.SetValue(Avalonia.Controls.Canvas.LeftProperty, (double)x);
                existing.SetValue(Avalonia.Controls.Canvas.TopProperty, (double)y);
                existing.SetValue(Avalonia.Controls.Canvas.ZIndexProperty, (int)z);
                _ = LoadLayerSourceAsync(id, existing, assetId);
                return;
            }

            var img = new AvaloniaImage { Opacity = 1, Stretch = Avalonia.Media.Stretch.Uniform };
            img.SetValue(Avalonia.Controls.Canvas.LeftProperty, (double)x);
            img.SetValue(Avalonia.Controls.Canvas.TopProperty, (double)y);
            img.SetValue(Avalonia.Controls.Canvas.ZIndexProperty, (int)z);

            _layers[id] = img;
            _gameScreen.LayerCanvas.Children.Add(img);
            _ = LoadLayerSourceAsync(id, img, assetId);
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

    public void MoveLayer(string id, float x, float y, float z, float durationSec)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_layers.TryGetValue(id, out var img))
                return;

            img.SetValue(Avalonia.Controls.Canvas.LeftProperty, (double)x);
            img.SetValue(Avalonia.Controls.Canvas.TopProperty, (double)y);
            img.SetValue(Avalonia.Controls.Canvas.ZIndexProperty, (int)z);
        });
    }

    public void RegisterWidget(string id, AvaloniaControl control)
    {
        _widgets[id] = control;
    }

    public void ShowControl(string instanceId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_widgets.TryGetValue(instanceId, out var ctrl))
                ctrl.IsVisible = true;
        });
    }

    public void HideControl(string instanceId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_widgets.TryGetValue(instanceId, out var ctrl))
                ctrl.IsVisible = false;
        });
    }

    public void SetControlProperty(string instanceId, string property, string value)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_widgets.TryGetValue(instanceId, out var ctrl))
                return;

            var prop = ctrl.GetType().GetProperty(property);
            if (prop?.CanWrite != true)
                return;

            var converted = ConvertValue(prop.PropertyType, value);
            if (converted != null)
                prop.SetValue(ctrl, converted);
        });
    }

    private static object? ConvertValue(Type targetType, string value)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(int) && int.TryParse(value, out var i)) return i;
        if (targetType == typeof(double) && double.TryParse(value, out var d)) return d;
        if (targetType == typeof(float) && float.TryParse(value, out var f)) return f;
        if (targetType == typeof(bool) && bool.TryParse(value, out var b)) return b;
        return null;
    }
}
