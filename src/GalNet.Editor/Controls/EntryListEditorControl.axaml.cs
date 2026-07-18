using Avalonia.Controls;
using Avalonia.Interactivity;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;

public partial class EntryListEditorControl : ReorderableListControl
{
    public EntryListEditorControl()
    {
        InitializeComponent();
        InitializeDragDrop(ItemsListBox);
    }

    private void OnAutocompleteLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: EntryParameterEditorItemViewModel field }
            && field.Id == "speaker"
            && DataContext is GroupEditorPanelViewModel viewModel)
            viewModel.CommitSpeaker(field.StringValue);
    }
}
