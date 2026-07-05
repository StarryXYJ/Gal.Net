using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Core.Widget;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultSlotConfig
{
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 120;
}

public sealed class DefaultSlotTemplate : Border, ISaveSlot
{
    public string Category => "SaveSlot";

    private readonly int _slotIndex;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _descBlock;

    public bool IsEmpty { get; private set; } = true;

    public event Action<int>? SlotSelected;

    public DefaultSlotTemplate(int slotIndex, DefaultSlotConfig? config = null)
    {
        _slotIndex = slotIndex;
        var cfg = config ?? new DefaultSlotConfig();

        Width = cfg.Width;
        Height = cfg.Height;
        Margin = new Avalonia.Thickness(8);
        CornerRadius = new Avalonia.CornerRadius(6);
        Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        BorderBrush = Brushes.Gray;
        BorderThickness = new Avalonia.Thickness(1);

        _titleBlock = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = Brushes.White,
        };
        _descBlock = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.LightGray,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };

        Child = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Avalonia.Thickness(8),
            Children = { _titleBlock, _descBlock },
        };

        PointerPressed += (_, _) => SlotSelected?.Invoke(_slotIndex);
    }

    public void SetSlotData(int slotIndex, DateTime? timestamp, string? description)
    {
        if (timestamp == null)
        {
            IsEmpty = true;
            _titleBlock.Text = $"Slot {slotIndex}";
            _descBlock.Text = "(Empty)";
            _descBlock.Foreground = Brushes.LightGray;
        }
        else
        {
            IsEmpty = false;
            _titleBlock.Text = timestamp.Value.ToString("yyyy/MM/dd HH:mm");
            _descBlock.Text = description ?? "";
            _descBlock.Foreground = Brushes.White;
        }
    }
}
