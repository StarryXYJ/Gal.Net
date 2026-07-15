using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;
using GalNet.Core.Services;
using GalNet.Core.Widget;

namespace GalNet.Control.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IGameScreenNavigator _navigator;
    public WidgetHostViewModel BgmVolumeHost { get; private set; } = null!;
    public WidgetHostViewModel SfxVolumeHost { get; private set; } = null!;
    public WidgetHostViewModel VoiceVolumeHost { get; private set; } = null!;
    public WidgetHostViewModel TextSpeedHost { get; private set; } = null!;
    public WidgetHostViewModel AutoDelayHost { get; private set; } = null!;
    public WidgetHostViewModel QuickDelayHost { get; private set; } = null!;
    public WidgetHostViewModel FullscreenHost { get; private set; } = null!;
    public WidgetHostViewModel BackButtonHost { get; private set; } = null!;

    public SettingsViewModel(ISettingsService settings, IGameScreenNavigator navigator) { _settings = settings; _navigator = navigator; }

    public void SetHosts(WidgetHostViewModel bgm, WidgetHostViewModel sfx, WidgetHostViewModel voice, WidgetHostViewModel textSpeed,
        WidgetHostViewModel autoDelay, WidgetHostViewModel quickDelay, WidgetHostViewModel fullscreen, WidgetHostViewModel back)
    {
        BgmVolumeHost = bgm; SfxVolumeHost = sfx; VoiceVolumeHost = voice; TextSpeedHost = textSpeed;
        AutoDelayHost = autoDelay; QuickDelayHost = quickDelay; FullscreenHost = fullscreen; BackButtonHost = back;
        ConfigureSlider(bgm, _settings.BgmVolume, 1, value => _settings.BgmVolume = (float)value);
        ConfigureSlider(sfx, _settings.SfxVolume, 1, value => _settings.SfxVolume = (float)value);
        ConfigureSlider(voice, _settings.VoiceVolume, 1, value => _settings.VoiceVolume = (float)value);
        ConfigureSlider(textSpeed, _settings.TextSpeed, 200, value => _settings.TextSpeed = (float)value);
        ConfigureSlider(autoDelay, _settings.GetSnapshot().AutoAdvanceInterval, 10, value => { var snapshot = _settings.GetSnapshot(); snapshot.AutoAdvanceInterval = (float)value; _settings.ApplySnapshot(snapshot); });
        ConfigureSlider(quickDelay, _settings.GetSnapshot().QuickAdvanceInterval, 2, value => { var snapshot = _settings.GetSnapshot(); snapshot.QuickAdvanceInterval = (float)value; _settings.ApplySnapshot(snapshot); });
        var toggle = fullscreen.RequireWidget<IToggleWidget>(); toggle.Label = ""; toggle.IsChecked = _settings.Fullscreen; toggle.CheckedChanged += value => _settings.Fullscreen = value;
        var backButton = back.RequireWidget<IButtonWidget>(); backButton.Text = "Back"; backButton.Command = BackCommand;
    }

    private static void ConfigureSlider(WidgetHostViewModel host, double value, double maximum, Action<double> changed)
    {
        var slider = host.RequireWidget<ISliderWidget>(); slider.Label = null; slider.Minimum = 0; slider.Maximum = maximum; slider.Value = value; slider.ValueChanged += changed;
    }

    [RelayCommand] private Task BackAsync() => _navigator.GoBackAsync();
}
