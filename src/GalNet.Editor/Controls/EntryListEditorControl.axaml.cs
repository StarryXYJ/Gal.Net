using Avalonia.Controls;
using Avalonia.Input;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;

public partial class EntryListEditorControl : ReorderableListControl
{
    public EntryListEditorControl()
    {
        InitializeComponent();
        InitializeDragDrop(ItemsListBox);
    }

    protected override void OnMoveItem(int fromIndex, int toIndex)
    {
        if (DataContext is GroupEditorPanelViewModel vm)
            vm.MoveEntryTo(vm.GroupNode.Entries[fromIndex], toIndex);
    }
}