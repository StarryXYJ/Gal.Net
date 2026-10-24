using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class FileDocumentPanelView : UserControl
{
    public FileDocumentPanelView() => InitializeComponent();
    private async void OnTextLostFocus(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not FileDocumentPanelViewModel vm) return;
        try { await vm.Buffer.CommitAsync(); vm.Buffer.CheckExternalChange(); } catch (Exception) { }
    }
    private async void OnTextKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.S || !args.KeyModifiers.HasFlag(KeyModifiers.Control) || DataContext is not FileDocumentPanelViewModel vm) return;
        args.Handled = true;
        try { await vm.Buffer.SaveCommand.ExecuteAsync(null); } catch (Exception) { }
    }
}
