using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using System.Windows.Input;

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

    public int SlotIndex { get => GetValue(SlotIndexProperty); set => SetValue(SlotIndexProperty, value); }
    public string? Timestamp { get => GetValue(TimestampProperty); set => SetValue(TimestampProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public IImage? Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public bool IsEmpty { get => GetValue(IsEmptyProperty); set => SetValue(IsEmptyProperty, value); }
    public bool IsCorrupt { get => GetValue(IsCorruptProperty); set => SetValue(IsCorruptProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
}
