using Avalonia;
using Avalonia.Controls;

namespace GalNet.Avalonia.GameView.Controls;

/// <summary>Reflows existing editors rather than recreating their bindings or drafts.</summary>
public sealed class SettingRow : Grid
{
    private bool? _compact;
    protected override Size MeasureOverride(Size availableSize)
    {
        var compact = availableSize.Width < 600;
        if (_compact != compact && Children.Count == 2)
        {
            _compact = compact;
            ColumnDefinitions = new(compact ? "*" : "220,*");
            RowDefinitions = new(compact ? "Auto,Auto" : "Auto");
            RowSpacing = compact ? 8 : 0;
            var editor = Children[1];
            var fullWidth = !Children[0].IsVisible;
            SetRow(editor, compact ? 1 : 0);
            SetColumn(editor, compact || fullWidth ? 0 : 1);
            SetColumnSpan(editor, compact || !fullWidth ? 1 : 2);
        }
        return base.MeasureOverride(availableSize);
    }
}
