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
    public Control CreateLayer(string assetId)
    {
        var path = Path.IsPathRooted(assetId) ? assetId : Path.Combine(assetRoot, assetId);
        try
        {
            if (File.Exists(path))
                return new Image { Source = new Bitmap(path), Stretch = Stretch.UniformToFill };

            GameLog.Logger.Warning("Layer asset was not found; rendering placeholder: {AssetId}", assetId);
        }
        catch (Exception exception)
        {
            GameLog.Logger.Warning(exception, "Layer asset could not be read; rendering placeholder: {AssetId}", assetId);
        }

        return new Border
        {
            Width = 240,
            Height = 240,
            Background = new SolidColorBrush(Color.Parse("#A82A2D42")),
            BorderBrush = new SolidColorBrush(Color.Parse("#C89591D8")),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Margin = new Thickness(16),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Missing layer", FontWeight = FontWeight.SemiBold, FontSize = 18 },
                    new TextBlock { Text = assetId, TextWrapping = TextWrapping.Wrap }
                }
            }
        };
    }
}
