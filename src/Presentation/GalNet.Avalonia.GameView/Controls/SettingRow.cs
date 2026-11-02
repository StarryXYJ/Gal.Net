using Avalonia;
using Avalonia.Controls;

namespace GalNet.Avalonia.GameView.Controls;

/// <summary>Reflows existing editors rather than recreating their bindings or drafts.</summary>
public sealed class SettingRow : Grid
{
    public SettingRow()
    {
        ColumnDefinitions = new("260,*");
        ColumnSpacing = 32;
        RowSpacing = 8;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width) && Children.Count == 2)
        {
            var fullWidth = !Children[0].IsVisible;
            var stacked = availableSize.Width < 600 && !fullWidth;
            if (ColumnDefinitions.Count != (stacked ? 1 : 2))
                ColumnDefinitions = new(stacked ? "*" : "260,*");
            if (RowDefinitions.Count != (stacked ? 2 : 1))
                RowDefinitions = new(stacked ? "Auto,Auto" : "Auto");
            var editor = Children[1];
            SetRow(editor, stacked ? 1 : 0);
            SetColumn(editor, stacked || fullWidth ? 0 : 1);
            SetColumnSpan(editor, fullWidth ? 2 : 1);
        }
        return base.MeasureOverride(availableSize);
    }
}
