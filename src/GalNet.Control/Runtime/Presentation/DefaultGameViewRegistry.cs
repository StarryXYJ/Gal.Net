using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GalNet.Control.Screen.BuiltIn;
using Serilog;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaImage = Avalonia.Controls.Image;

namespace GalNet.Control.View;

internal sealed class DefaultGameViewRegistry
{
    private readonly GameScreenView _gameScreen;
    private readonly Dictionary<string, AvaloniaImage> _layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AvaloniaControl> _widgets = new(StringComparer.OrdinalIgnoreCase);

    public DefaultGameViewRegistry(GameScreenView gameScreen)
    {
        _gameScreen = gameScreen;
    }

    public void ShowLayer(string id, string assetId, float x, float y, float z = 0)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_layers.TryGetValue(id, out var existing))
            {
                existing.SetValue(Avalonia.Controls.Canvas.LeftProperty, (double)x);
                existing.SetValue(Avalonia.Controls.Canvas.TopProperty, (double)y);
                existing.SetValue(Avalonia.Controls.Canvas.ZIndexProperty, (int)z);
                return;
            }

            var img = new AvaloniaImage { Opacity = 1, Stretch = Avalonia.Media.Stretch.Uniform };
            img.SetValue(Avalonia.Controls.Canvas.LeftProperty, (double)x);
            img.SetValue(Avalonia.Controls.Canvas.TopProperty, (double)y);
            img.SetValue(Avalonia.Controls.Canvas.ZIndexProperty, (int)z);

            // 尝试按 assetId 路径加载图片
            if (!string.IsNullOrEmpty(assetId))
            {
                try
                {
                    var bitmap = new Bitmap(assetId);
                    img.Source = bitmap;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load layer image: {Path}", assetId);
                }
            }

            _layers[id] = img;
            _gameScreen.LayerCanvas.Children.Add(img);
        });
    }

    public void HideLayer(string id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_layers.Remove(id, out var img))
                _gameScreen.LayerCanvas.Children.Remove(img);
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
