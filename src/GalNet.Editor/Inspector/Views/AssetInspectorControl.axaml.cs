using Avalonia.Controls;
using Avalonia.Input;
using GalNet.Editor.Inspector.ViewModels;
namespace GalNet.Editor.Inspector.Views;
public partial class AssetInspectorControl : UserControl
{
    public AssetInspectorControl() => InitializeComponent();
    private void OnAudioSeekStarted(object? sender, PointerPressedEventArgs e) => (DataContext as AssetInspectorControlViewModel)?.BeginAudioSeek();
    private void OnAudioSeekCompleted(object? sender, PointerReleasedEventArgs e) { if (sender is Slider slider) (DataContext as AssetInspectorControlViewModel)?.EndAudioSeek(slider.Value); }
}
