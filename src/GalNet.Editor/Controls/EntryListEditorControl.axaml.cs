using Avalonia.Controls;

namespace GalNet.Editor.Controls;

public partial class EntryListEditorControl : ReorderableListControl
{
    public EntryListEditorControl()
    {
        InitializeComponent();
        InitializeDragDrop(ItemsListBox);
    }
}
