using Avalonia.Controls;
using Avalonia.Interactivity;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class UiPaletteResetWindow : Window
{
    public string SelectedPaletteId => (DataContext as UiPaletteResetViewModel)?.SelectedPaletteId ?? string.Empty;

    public UiPaletteResetWindow(string selectedPaletteId)
    {
        InitializeComponent();
        DataContext = new UiPaletteResetViewModel(selectedPaletteId);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
