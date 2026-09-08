using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GalNet.Avalonia.GameView.Presentation;

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
        }
        catch (Exception)
        {
            // The reference client shows an explicit placeholder for missing visual assets.
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#551B1C27")),
            Child = new TextBlock { Text = assetId, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap }
        };
    }
}
