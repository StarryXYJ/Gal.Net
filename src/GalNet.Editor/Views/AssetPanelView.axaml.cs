using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Editor.Models;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;
public partial class AssetPanelView : UserControl
{
    public AssetPanelView() => InitializeComponent();
    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: AssetEntry entry } && DataContext is AssetPanelViewModel vm)
            vm.OpenCommand.Execute(entry);
    }
    private void OnPathLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && DataContext is AssetPanelViewModel vm) vm.NavigatePathCommand.Execute(box.Text);
    }
    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box && DataContext is AssetPanelViewModel vm) { vm.NavigatePathCommand.Execute(box.Text); e.Handled = true; }
    }
}
