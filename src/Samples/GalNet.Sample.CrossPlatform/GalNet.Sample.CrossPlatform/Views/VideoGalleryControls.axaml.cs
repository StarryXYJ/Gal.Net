using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.Presentation;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class VideoGalleryControls : UserControl
{
    private bool? _narrow;
    public VideoGalleryControls()
    {
        InitializeComponent();
        Seek.AddHandler(PointerPressedEvent, (_, _) => (DataContext as GalleryVideoState)?.BeginSeek(), RoutingStrategies.Tunnel);
        Seek.AddHandler(PointerReleasedEvent, (_, _) => (DataContext as GalleryVideoState)?.EndSeek(), RoutingStrategies.Bubble, handledEventsToo: true);
        Seek.PointerCaptureLost += (_, _) => (DataContext as GalleryVideoState)?.EndSeek();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var narrow = availableSize.Width < 420;
        if (_narrow != narrow)
        {
            _narrow = narrow;
            ControlsGrid.ColumnDefinitions = new(narrow ? "Auto,*" : "Auto,*,Auto");
            ControlsGrid.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
            Grid.SetRow(TimeLabel, narrow ? 1 : 0);
            Grid.SetColumn(TimeLabel, narrow ? 0 : 2);
            Grid.SetColumnSpan(TimeLabel, narrow ? 2 : 1);
            TimeLabel.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right;
        }
        return base.MeasureOverride(availableSize);
    }
}
