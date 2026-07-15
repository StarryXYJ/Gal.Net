using Avalonia.Controls;

namespace GalNet.Editor.Controls;
public partial class BranchOptionListEditorControl : ReorderableListControl
{
    public BranchOptionListEditorControl() { InitializeComponent(); InitializeDragDrop(ItemsListBox); }
}
