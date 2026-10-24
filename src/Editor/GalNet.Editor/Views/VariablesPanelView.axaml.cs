using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class VariablesPanelView : UserControl
{
    private bool? _narrow;
    public VariablesPanelView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 560;
            if (_narrow == narrow) return;
            _narrow = narrow;
            VariableColumns.ColumnDefinitions = new(narrow ? "*" : "220,10,*");
            VariableColumns.RowDefinitions = new(narrow ? "160,8,*" : "*");
            for (var i = 0; i < VariableColumns.Children.Count; i++)
            { Grid.SetColumn(VariableColumns.Children[i], narrow ? 0 : i); Grid.SetRow(VariableColumns.Children[i], narrow ? i : 0); }
            if (VariableColumns.Children[1] is GridSplitter splitter) splitter.ResizeDirection = narrow ? GridResizeDirection.Rows : GridResizeDirection.Columns;
        };
    }
    private void OnVariableAdded(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (DataContext is not VariablesPanelViewModel { Selected: { } selected }) return;
        VariableRows.ScrollIntoView(selected); NameInput.Focus(); NameInput.SelectAll();
    });
}
