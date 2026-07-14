using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Core.Screen;

namespace GalNet.Control.Screen.BuiltIn;

/// <summary>
/// Config for settings screen — defines which widget types to use.
/// </summary>
public sealed class SettingsScreenConfig
{
    public double BgmVolume { get; set; } = 0.8;
    public double SfxVolume { get; set; } = 0.8;
    public double VoiceVolume { get; set; } = 1.0;
    public double TextSpeed { get; set; } = 40;
    public bool Fullscreen { get; set; } = false;
}

/// <summary>
/// Settings screen — implements ISettingsScreen. XAML layout in SettingsScreenView.axaml.
/// Supports standalone config mode or ViewModel binding.
/// </summary>
public partial class SettingsScreenView : UserControl, ISettingsScreen
{
    public string Category => "Settings";
    public event Action? BackRequested;

    public double BgmVolume { get; set; }
    public double SfxVolume { get; set; }
    public double VoiceVolume { get; set; }
    public double TextSpeed { get; set; }
    public bool Fullscreen { get; set; }

    public SettingsScreenView()
    {
        InitializeComponent();
        BackButton.Click += (_, _) => BackRequested?.Invoke();
    }

    public SettingsScreenView(SettingsScreenConfig config) : this()
    {
        BgmVolume = config.BgmVolume;
        SfxVolume = config.SfxVolume;
        VoiceVolume = config.VoiceVolume;
        TextSpeed = config.TextSpeed;
        Fullscreen = config.Fullscreen;

        SettingsPanel.Children.Add(CreateSliderRow("BGM Volume", BgmVolume, v => BgmVolume = v));
        SettingsPanel.Children.Add(CreateSliderRow("SFX Volume", SfxVolume, v => SfxVolume = v));
        SettingsPanel.Children.Add(CreateSliderRow("Voice Volume", VoiceVolume, v => VoiceVolume = v));
        SettingsPanel.Children.Add(CreateSliderRow("Text Speed", TextSpeed / 100.0, v => TextSpeed = v * 100, 100));
        SettingsPanel.Children.Add(CreateToggleRow("Fullscreen", Fullscreen, v => Fullscreen = v));
    }

    /// <summary>Bind to ViewModel — sliders/toggles wired to ISettingsService.</summary>
    public void BindToViewModel(SettingsScreenViewModel vm)
    {
        DataContext = vm;

        SettingsPanel.Children.Add(CreateSliderRow("BGM Volume", vm.BgmVolume,
            v => vm.BgmVolume = (float)v, 100));
        SettingsPanel.Children.Add(CreateSliderRow("SFX Volume", vm.SfxVolume,
            v => vm.SfxVolume = (float)v, 100));
        SettingsPanel.Children.Add(CreateSliderRow("Voice Volume", vm.VoiceVolume,
            v => vm.VoiceVolume = (float)v, 100));
        SettingsPanel.Children.Add(CreateSliderRow("Text Speed", vm.TextSpeed,
            v => vm.TextSpeed = (float)v, 200));
        SettingsPanel.Children.Add(CreateToggleRow("Fullscreen", vm.Fullscreen,
            v => vm.Fullscreen = v));

        BackButton.Click += (_, _) => vm.Back();
    }

    private static StackPanel CreateSliderRow(string label, double value, Action<double> onChange, double max = 1)
    {
        var slider = new Slider { Minimum = 0, Maximum = max, Value = value, Width = 200 };
        var valueText = new TextBlock { Width = 60, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, _) => { valueText.Text = max == 1 ? $"{(int)(slider.Value * 100)}%" : $"{(int)slider.Value}"; onChange(slider.Value); };
        valueText.Text = max == 1 ? $"{(int)(value * 100)}%" : $"{(int)value}";

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = label, Width = 110, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
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
                new TextBlock { Text = label, Width = 110, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
                cb,
            },
        };
    }
}
