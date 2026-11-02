using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace GalNet.Game.Controls;

/// <summary>ListBox-based choice control whose host owns the resulting game action.</summary>
public class ChoiceList : ListBox
{
    public static readonly StyledProperty<ICommand?> SelectionCommandProperty =
        AvaloniaProperty.Register<ChoiceList, ICommand?>(nameof(SelectionCommand));

    public ICommand? SelectionCommand
    {
        get => GetValue(SelectionCommandProperty);
        set => SetValue(SelectionCommandProperty, value);
    }

    public event EventHandler<int>? ChoiceSelected;

    /// <summary>Move the highlight without committing a story branch.</summary>
    public void PreviewSelection(int index)
    {
        SelectedIndex = index;
        if (index >= 0) ScrollIntoView(index);
    }

    public void CommitSelection()
    {
        if (SelectedIndex < 0) return;
        if (SelectionCommand?.CanExecute(SelectedIndex) == true)
            SelectionCommand.Execute(SelectedIndex);
        ChoiceSelected?.Invoke(this, SelectedIndex);
    }

    public ChoiceList()
    {
        Focusable = true;
        AddHandler(KeyDownEvent, ConfirmKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(TappedEvent, ConfirmTap, handledEventsToo: true);
    }

    private void ConfirmKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        CommitSelection();
        e.Handled = true;
    }

    private void ConfirmTap(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is { IsEffectivelyEnabled: true } item &&
            ReferenceEquals(item.GetVisualAncestors().OfType<ChoiceList>().FirstOrDefault(), this))
        {
            SelectedIndex = IndexFromContainer(item);
            CommitSelection();
            e.Handled = true;
        }
    }
}
