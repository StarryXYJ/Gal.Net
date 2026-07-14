using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Control.ViewModels;

namespace GalNet.Control.Views;

/// <summary>
/// 设置页 View —— 手动构建滑块和开关，回调 SettingsViewModel。
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SettingsViewModel vm)
        {
            SettingsPanel.Children.Clear();

            SettingsPanel.Children.Add(CreateSliderRow("BGM Volume", vm.BgmVolume, v => vm.BgmVolume = (float)v, 1f));
            SettingsPanel.Children.Add(CreateSliderRow("SFX Volume", vm.SfxVolume, v => vm.SfxVolume = (float)v, 1f));
            SettingsPanel.Children.Add(CreateSliderRow("Voice Volume", vm.VoiceVolume, v => vm.VoiceVolume = (float)v, 1f));
            SettingsPanel.Children.Add(CreateSliderRow("Text Speed", vm.TextSpeed, v => vm.TextSpeed = (float)v, 200));
            SettingsPanel.Children.Add(CreateSliderRow("Auto Delay", vm.AutoAdvanceInterval, v => vm.AutoAdvanceInterval = (float)v, 10));
            SettingsPanel.Children.Add(CreateSliderRow("Quick Delay", vm.QuickAdvanceInterval, v => vm.QuickAdvanceInterval = (float)v, 2));
            SettingsPanel.Children.Add(CreateToggleRow("Fullscreen", vm.Fullscreen, v => vm.Fullscreen = v));

            BackButton.Click += (_, _) => vm.Back();
        }
    }

    private static StackPanel CreateSliderRow(string label, double value, Action<double> onChange, double max)
    {
        var slider = new Slider { Minimum = 0, Maximum = max, Value = value, Width = 200 };
        var valueText = new TextBlock
        {
            Width = 60,
            Foreground = Brushes.LightGray,
            VerticalAlignment = VerticalAlignment.Center,
        };

        slider.ValueChanged += (_, _) =>
        {
            if (max == 1f)
                valueText.Text = $"{(int)(slider.Value * 100)}%";
            else
                valueText.Text = $"{(int)slider.Value}";
            onChange(slider.Value);
        };

        valueText.Text = max == 1f ? $"{(int)(value * 100)}%" : $"{(int)value}";

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Width = 110,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                slider,
                valueText,
            },
        };
    }

    private static StackPanel CreateToggleRow(string label, bool isChecked, Action<bool> onChange)
    {
        var cb = new CheckBox { IsChecked = isChecked };
        cb.IsCheckedChanged += (_, _) => onChange(cb.IsChecked == true);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Width = 110,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                cb,
            },
        };
    }
}
