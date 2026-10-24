using Avalonia.Controls;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class LogPanelView : UserControl
{
    private void OnLogDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs args)
    { if (sender is Control { DataContext: GalNet.Editor.Services.LogLine line }) _vm?.Navigate(line); }
    private LogPanelViewModel? _vm;

    public LogPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
            _vm.ScrollToEndRequested -= OnScrollToEnd;

        _vm = DataContext as LogPanelViewModel;

        if (_vm is not null)
            _vm.ScrollToEndRequested += OnScrollToEnd;
    }

    private void OnScrollToEnd()
    {
        if (LogListBox.ItemCount > 0)
            LogListBox.ScrollIntoView(LogListBox.ItemCount - 1);
    }
}
