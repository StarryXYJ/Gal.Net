using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Core.Widget;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultSliderConfig
{
    public double Minimum { get; set; } = 0;
    public double Maximum { get; set; } = 100;
    public double Value { get; set; } = 50;
    public double Step { get; set; } = 1;
    public string? Label { get; set; }
    public bool ShowValue { get; set; } = true;
}

public sealed class DefaultSliderTemplate : StackPanel, ISliderWidget
{
    public string Category => "Slider";

    private readonly Slider _slider;
    private readonly TextBlock _valueText;

    public event Action<double>? ValueChanged;

    public double Value
    {
        get => _slider.Value;
        set => _slider.Value = value;
    }

    public double Minimum
    {
        get => _slider.Minimum;
        set => _slider.Minimum = value;
    }

    public double Maximum
    {
        get => _slider.Maximum;
        set => _slider.Maximum = value;
    }

    public DefaultSliderTemplate(DefaultSliderConfig? config = null)
    {
        var cfg = config ?? new DefaultSliderConfig();

        Orientation = Orientation.Horizontal;
        Spacing = 8;

        if (cfg.Label != null)
        {
            Children.Add(new TextBlock
            {
                Text = cfg.Label,
                Width = 100,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        _slider = new Slider
        {
            Minimum = cfg.Minimum,
            Maximum = cfg.Maximum,
            Value = cfg.Value,
            SmallChange = cfg.Step,
            LargeChange = cfg.Step * 10,
            Width = 200,
        };
        _slider.ValueChanged += (_, _) =>
        {
            UpdateValueDisplay();
            ValueChanged?.Invoke(_slider.Value);
        };
        Children.Add(_slider);

        _valueText = new TextBlock
        {
            Width = 60,
            Foreground = Brushes.LightGray,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (cfg.ShowValue) Children.Add(_valueText);
        UpdateValueDisplay();
    }

    private void UpdateValueDisplay()
    {
        _valueText.Text = $"{(int)_slider.Value}";
    }
}
