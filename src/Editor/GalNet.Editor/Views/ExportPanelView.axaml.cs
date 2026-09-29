using Avalonia.Controls;
using Avalonia.Interactivity;
namespace GalNet.Editor.Views;
public partial class ExportPanelView : UserControl
{
    public ExportPanelView() => InitializeComponent();

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is ExportWindow window && DataContext is ViewModels.ExportPanelViewModel viewModel)
            await window.ExportAsync(viewModel);
    }
}
