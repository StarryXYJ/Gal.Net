using Avalonia.Controls;

namespace GalNet.Editor.Controls;
public partial class BranchConditionListEditorControl : ReorderableListControl
{
    public BranchConditionListEditorControl() { InitializeComponent(); InitializeDragDrop(ItemsListBox); }
}
