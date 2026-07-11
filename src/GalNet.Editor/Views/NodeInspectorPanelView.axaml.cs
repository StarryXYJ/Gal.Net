using System;
using System.Linq;
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
            _optionsDragHelper = CreateDragHelper(
                OptionsListBox,
                (from, to) => vm.Workspace.MoveChoiceOptionTo(
                    vm.Workspace.SelectedNode?.Options[from]!,
                    to));

            _conditionsDragHelper = CreateDragHelper(
                ConditionsListBox,
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
        if (_optionsDragHelper is not null)
            _optionsDragHelper.OnPointerMoved(e);

        if (_conditionsDragHelper is not null)
            _conditionsDragHelper.OnPointerMoved(e);
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_optionsDragHelper is not null)
            _optionsDragHelper.OnPointerReleased(e);

        if (_conditionsDragHelper is not null)
            _conditionsDragHelper.OnPointerReleased(e);
    }

    private DragDropHelper CreateDragHelper(ListBox listBox, Action<int, int> onMove) =>
        new(listBox, this, onMove);
}
