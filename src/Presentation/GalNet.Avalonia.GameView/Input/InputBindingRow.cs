using Avalonia;
using Avalonia.Controls;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>Keeps both capture controls alive while stacking their rows on narrow pages.</summary>
internal sealed class InputBindingRow : Grid
{
    private bool? _compact;
    protected override Size MeasureOverride(Size availableSize)
    {
        var compact = availableSize.Width < 640;
        if (_compact != compact && Children.Count == 4)
        {
            _compact = compact;
            ColumnDefinitions = new(compact ? "*,Auto" : "180,*,*,Auto");
            RowDefinitions = new(compact ? "Auto,Auto,Auto" : "Auto");
            RowSpacing = compact ? 8 : 0;
            SetColumnSpan(Children[0], compact ? 2 : 1);
            for (var index = 1; index <= 2; index++)
            {
                SetRow(Children[index], compact ? index : 0);
                SetColumn(Children[index], compact ? 0 : index);
            }
            SetRow(Children[3], compact ? 1 : 0);
            SetColumn(Children[3], compact ? 1 : 3);
            SetRowSpan(Children[3], compact ? 2 : 1);
            Children[3].VerticalAlignment = compact ? global::Avalonia.Layout.VerticalAlignment.Center : global::Avalonia.Layout.VerticalAlignment.Stretch;
        }
        return base.MeasureOverride(availableSize);
    }
}
