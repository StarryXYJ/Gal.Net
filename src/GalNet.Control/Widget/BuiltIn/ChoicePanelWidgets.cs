using Avalonia.Controls;
using Avalonia.Layout;
using GalNet.Core.Widget;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultChoiceConfig
{
    public double ButtonSpacing { get; set; } = 8;
    public double FontSize { get; set; } = 16;
    public double ButtonWidth { get; set; } = 200;
    public double ButtonHeight { get; set; } = 40;
}

public sealed class DefaultChoiceTemplate : StackPanel, IChoicePanel
{
    public string Category => "ChoicePanel";

    public event Action<int>? ChoiceSelected;

    private readonly DefaultChoiceConfig _config;

    public DefaultChoiceTemplate(DefaultChoiceConfig? config = null)
    {
        _config = config ?? new DefaultChoiceConfig();

        Orientation = Orientation.Vertical;
        Spacing = _config.ButtonSpacing;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public void SetChoices(string[] options)
    {
        Children.Clear();
        for (int i = 0; i < options.Length; i++)
        {
            var index = i;
            var btn = new Button
            {
                Content = $"[{i + 1}] {options[i]}",
                FontSize = _config.FontSize,
                MinWidth = _config.ButtonWidth,
                MinHeight = _config.ButtonHeight,
                Margin = new Avalonia.Thickness(4),
            };
            btn.Click += (_, _) => ChoiceSelected?.Invoke(index);
            Children.Add(btn);
        }
    }
}

public sealed class HorizontalChoiceConfig
{
    public double ButtonSpacing { get; set; } = 12;
    public double FontSize { get; set; } = 16;
    public double ButtonWidth { get; set; } = 160;
    public double ButtonHeight { get; set; } = 50;
}

public sealed class HorizontalChoiceTemplate : StackPanel, IChoicePanel
{
    public string Category => "ChoicePanel";

    public event Action<int>? ChoiceSelected;

    private readonly HorizontalChoiceConfig _config;

    public HorizontalChoiceTemplate(HorizontalChoiceConfig? config = null)
    {
        _config = config ?? new HorizontalChoiceConfig();

        Orientation = Orientation.Horizontal;
        Spacing = _config.ButtonSpacing;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
    }

    public void SetChoices(string[] options)
    {
        Children.Clear();
        for (int i = 0; i < options.Length; i++)
        {
            var index = i;
            var btn = new Button
            {
                Content = options[i],
                FontSize = _config.FontSize,
                MinWidth = _config.ButtonWidth,
                MinHeight = _config.ButtonHeight,
            };
            btn.Click += (_, _) => ChoiceSelected?.Invoke(index);
            Children.Add(btn);
        }
    }
}
