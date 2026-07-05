using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Core.Widget;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultToggleConfig
{
    public string Label { get; set; } = "";
    public bool IsChecked { get; set; } = false;
}

public sealed class DefaultToggleTemplate : StackPanel, IToggleWidget
{
    public string Category => "Toggle";

    private readonly CheckBox _checkBox;

    public event Action<bool>? CheckedChanged;

    public bool IsChecked
    {
        get => _checkBox.IsChecked == true;
        set => _checkBox.IsChecked = value;
    }

    public DefaultToggleTemplate(DefaultToggleConfig? config = null)
    {
        var cfg = config ?? new DefaultToggleConfig();

        Orientation = Orientation.Horizontal;
        Spacing = 8;

        _checkBox = new CheckBox
        {
            IsChecked = cfg.IsChecked,
        };
        _checkBox.IsCheckedChanged += (_, _) =>
        {
            CheckedChanged?.Invoke(_checkBox.IsChecked == true);
        };

        Children.Add(_checkBox);
        Children.Add(new TextBlock
        {
            Text = cfg.Label,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    public void SetLabel(string text)
    {
        if (Children.Count > 1 && Children[1] is TextBlock tb)
            tb.Text = text;
    }
}
