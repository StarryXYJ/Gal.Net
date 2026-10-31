using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.ViewModels;
namespace GalNet.Avalonia.GameView.Controls;

public partial class AudioGalleryRow : UserControl
{
    public static readonly StyledProperty<ICommand?> ToggleCommandProperty = AvaloniaProperty.Register<AudioGalleryRow, ICommand?>(nameof(ToggleCommand));
    public ICommand? ToggleCommand { get => GetValue(ToggleCommandProperty); set => SetValue(ToggleCommandProperty, value); }
    private bool? _narrow;
    public AudioGalleryRow()
    {
        InitializeComponent();
        Seek.AddHandler(PointerPressedEvent, (_, _) => (DataContext as GalleryEntryViewModel)?.BeginSeek(), RoutingStrategies.Tunnel);
        Seek.AddHandler(PointerReleasedEvent, (_, _) => (DataContext as GalleryEntryViewModel)?.EndSeek(), RoutingStrategies.Bubble, handledEventsToo: true);
        Seek.PointerCaptureLost += (_, _) => (DataContext as GalleryEntryViewModel)?.EndSeek();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateLayoutMode(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }
    private void UpdateLayoutMode(double width)
    {
        var narrow = width < 540;
        if (_narrow == narrow) return; _narrow = narrow;
        RowGrid.ColumnDefinitions = new(narrow ? "*,0,Auto" : "200,*,Auto");
        RowGrid.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        Grid.SetRow(Timeline, narrow ? 1 : 0); Grid.SetColumn(Timeline, narrow ? 0 : 1);
        Grid.SetRowSpan(Actions, narrow ? 2 : 1);
    }
}
