using Avalonia.Controls;
using Avalonia.Input;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;

public partial class VariableListEditorControl : ReorderableListControl
{
    public VariableListEditorControl()
    {
        InitializeComponent();
        InitializeDragDrop(ItemsListBox);
    }

    protected override void OnMoveItem(int fromIndex, int toIndex)
    {
        if (DataContext is VariableListEditorViewModel vm)
            vm.MoveItem(vm.Items[fromIndex], toIndex);
    }
}