using Avalonia.Controls;
using Avalonia.Input;
using GalNet.Editor.Controls;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class NodeInspectorPanelView : UserControl
{
    private DragDropHelper? _optionsDragHelper;
    private DragDropHelper? _conditionsDragHelper;

    public NodeInspectorPanelView()
    {
        InitializeComponent();
        AddHandler(PointerMovedEvent, OnDragPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (DataContext is NodeInspectorPanelViewModel vm)
        {
            _optionsDragHelper = new DragDropHelper(
                OptionsListBox,
                this,
                (from, to) => vm.Workspace.MoveChoiceOptionTo(
                    vm.Workspace.SelectedNode?.Options[from]!,
                    to));

            _conditionsDragHelper = new DragDropHelper(
                ConditionsListBox,
                this,
                (from, to) => vm.Workspace.MoveConditionTo(
                    vm.Workspace.SelectedNode?.Conditions[from]!,
                    to));
        }
    }

    private void OnChoiceOptionDragPointerPressed(object? sender, PointerPressedEventArgs e)
        => _optionsDragHelper?.OnDragHandlePressed(sender, e);

    private void OnConditionDragPointerPressed(object? sender, PointerPressedEventArgs e)
        => _conditionsDragHelper?.OnDragHandlePressed(sender, e);

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        _optionsDragHelper?.OnPointerMoved(e);
        _conditionsDragHelper?.OnPointerMoved(e);
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _optionsDragHelper?.OnPointerReleased(e);
        _conditionsDragHelper?.OnPointerReleased(e);
    }
}