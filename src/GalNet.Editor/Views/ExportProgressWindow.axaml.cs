using Avalonia.Interactivity;
using Ursa.Controls;

namespace GalNet.Editor.Views;

public partial class ExportProgressWindow : UrsaWindow
{
    public ExportProgressWindow() => InitializeComponent();

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
