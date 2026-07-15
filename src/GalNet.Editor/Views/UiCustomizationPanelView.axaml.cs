using Avalonia.Controls;
using Avalonia.Interactivity;
namespace GalNet.Editor.Views;
public partial class UiCustomizationPanelView : UserControl
{
    public UiCustomizationPanelView() => InitializeComponent();

    private async void OnResetPaletteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.UiCustomizationPanelViewModel viewModel || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var dialog = new UiPaletteResetWindow(viewModel.CurrentColorPaletteId);
        if (await dialog.ShowDialog<bool>(owner))
            await viewModel.ApplyColorPaletteAsync(dialog.SelectedPaletteId);
    }
}
