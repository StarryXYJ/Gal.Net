using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

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

    public ChoiceList() => SelectionChanged += OnSelectionChanged;

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedIndex < 0) return;

        if (SelectionCommand?.CanExecute(SelectedIndex) == true)
            SelectionCommand.Execute(SelectedIndex);
        ChoiceSelected?.Invoke(this, SelectedIndex);
    }
}
