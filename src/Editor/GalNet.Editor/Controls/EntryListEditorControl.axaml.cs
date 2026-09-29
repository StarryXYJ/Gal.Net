using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;

public partial class EntryListEditorControl : ReorderableListControl
{
    public EntryListEditorControl()
    {
        InitializeComponent();
        InitializeDragDrop(ItemsListBox);
        AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        ItemsListBox.SelectionChanged += (_, _) => ScrollSelectionIntoView();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || DataContext is not GroupEditorPanelViewModel viewModel || IsTextEditing(e.Source))
            return;

        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            viewModel.AppendEntryCommand.Execute(null);
        else if (e.KeyModifiers == KeyModifiers.Shift)
            viewModel.AddEntryCommand.Execute(null);
        else
            return;

        ScrollSelectionIntoView();
        e.Handled = true;
    }

    private static bool IsTextEditing(object? source)
    {
        if (source is not Avalonia.Controls.Control control) return false;
        return control is TextBox or NumericUpDown ||
               control.GetVisualAncestors().Any(ancestor => ancestor is TextBox or NumericUpDown);
    }

    private void ScrollSelectionIntoView()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is GroupEditorPanelViewModel { SelectedEntry: { } entry })
                ItemsListBox.ScrollIntoView(entry);
        }, DispatcherPriority.Loaded);
    }

    private void OnAutocompleteLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: EntryParameterEditorItemViewModel field }
            && field.Id == "speaker"
            && DataContext is GroupEditorPanelViewModel viewModel)
            viewModel.CommitSpeaker(field.StringValue);
    }
}
