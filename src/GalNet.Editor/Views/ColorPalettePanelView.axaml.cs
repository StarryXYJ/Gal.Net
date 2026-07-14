using Avalonia.Controls;
using Avalonia.Interactivity;
using GalNet.Editor.ViewModels;
namespace GalNet.Editor.Views;
public partial class ColorPalettePanelView : UserControl
{
    public ColorPalettePanelView() => InitializeComponent();
    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ColorItem item } && DataContext is ColorPalettePanelViewModel vm && vm.DeleteCommand.CanExecute(item))
            vm.DeleteCommand.Execute(item);
    }
}
