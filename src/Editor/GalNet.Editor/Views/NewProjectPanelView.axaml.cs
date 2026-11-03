using Avalonia.Controls;
using GalNet.Editor.ViewModels;
using Avalonia.Interactivity;

namespace GalNet.Editor.Views;

public partial class NewProjectPanelView : UserControl
{
    public NewProjectPanelView()
    {
        InitializeComponent();
    }
    private string? _applicationBeforeEdit;
    private void OnApplicationFocus(object? sender, RoutedEventArgs e) => _applicationBeforeEdit = (sender as TextBox)?.Text;
    private async void OnApplicationLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox input && input.Text != _applicationBeforeEdit && DataContext is NewProjectPanelViewModel vm)
            await vm.OfferDefaultApplicationAsync();
    }
}
