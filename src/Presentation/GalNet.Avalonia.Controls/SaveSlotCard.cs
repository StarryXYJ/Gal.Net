using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace GalNet.Game.Controls;

/// <summary>Templatable data-bound representation of one save/load slot.</summary>
public class SaveSlotCard : TemplatedControl
{
    public static readonly StyledProperty<int> SlotIndexProperty =
        AvaloniaProperty.Register<SaveSlotCard, int>(nameof(SlotIndex));
    public static readonly StyledProperty<string?> TimestampProperty =
        AvaloniaProperty.Register<SaveSlotCard, string?>(nameof(Timestamp));
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SaveSlotCard, string?>(nameof(Description));
    public static readonly StyledProperty<IImage?> PreviewProperty =
        AvaloniaProperty.Register<SaveSlotCard, IImage?>(nameof(Preview));
    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<SaveSlotCard, bool>(nameof(IsEmpty), true);
    public static readonly StyledProperty<bool> IsCorruptProperty =
        AvaloniaProperty.Register<SaveSlotCard, bool>(nameof(IsCorrupt));
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<SaveSlotCard, ICommand?>(nameof(Command));
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<SaveSlotCard, object?>(nameof(CommandParameter));
    public static readonly StyledProperty<ICommand?> RenameCommandProperty =
        AvaloniaProperty.Register<SaveSlotCard, ICommand?>(nameof(RenameCommand));
    public static readonly StyledProperty<bool> CanRenameProperty =
        AvaloniaProperty.Register<SaveSlotCard, bool>(nameof(CanRename));
    public static readonly StyledProperty<string?> RenameErrorProperty =
        AvaloniaProperty.Register<SaveSlotCard, string?>(nameof(RenameError));
    private TextBox? _name;
    private Button? _activate;

    public int SlotIndex { get => GetValue(SlotIndexProperty); set => SetValue(SlotIndexProperty, value); }
    public string? Timestamp { get => GetValue(TimestampProperty); set => SetValue(TimestampProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public IImage? Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public bool IsEmpty { get => GetValue(IsEmptyProperty); set => SetValue(IsEmptyProperty, value); }
    public bool IsCorrupt { get => GetValue(IsCorruptProperty); set => SetValue(IsCorruptProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public ICommand? RenameCommand { get => GetValue(RenameCommandProperty); set => SetValue(RenameCommandProperty, value); }
    public bool CanRename { get => GetValue(CanRenameProperty); set => SetValue(CanRenameProperty, value); }
    public string? RenameError { get => GetValue(RenameErrorProperty); set => SetValue(RenameErrorProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_name is not null) { _name.LostFocus -= OnNameLostFocus; _name.KeyDown -= OnNameKeyDown; }
        base.OnApplyTemplate(e);
        _name = e.NameScope.Find<TextBox>("PART_Name");
        _activate = e.NameScope.Find<Button>("PART_Activate");
        if (_name is not null) { _name.LostFocus += OnNameLostFocus; _name.KeyDown += OnNameKeyDown; }
    }

    private void OnNameLostFocus(object? sender, RoutedEventArgs e) => CommitName();
    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        if (_name?.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText)) == true) return;
        e.Handled = true;
        if (e.Key == Key.Escape && _name is not null) _name.Text = Description;
        else CommitName();
        _activate?.Focus();
    }
    private void CommitName()
    {
        var name = _name?.Text ?? "";
        if (CanRename && (name != Description || RenameError is not null) && RenameCommand?.CanExecute(name) == true)
            RenameCommand.Execute(name);
    }
}
