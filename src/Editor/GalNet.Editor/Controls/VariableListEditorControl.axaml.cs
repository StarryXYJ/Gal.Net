using Avalonia.Controls;

namespace GalNet.Editor.Controls;

public partial class VariableListEditorControl : ReorderableListControl
{
    public VariableListEditorControl()
    {
        InitializeComponent();
        InitializeDragDrop(ItemsListBox);
    }
}
