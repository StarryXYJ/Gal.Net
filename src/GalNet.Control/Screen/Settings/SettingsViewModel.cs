using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Services;
using GalNet.Core.UI;

namespace GalNet.Control.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings; private readonly IGameScreenNavigator _navigator;
    public SettingsUiConfiguration Configuration { get; }
    public IBrush BackgroundBrush => new SolidColorBrush(Configuration.BackgroundColor);
    public IBrush PanelBrush => new SolidColorBrush(Configuration.PanelColor);
    public IBrush TextBrush => new SolidColorBrush(Configuration.TextColor);
    public IBrush ButtonBrush => new SolidColorBrush(Configuration.ButtonColor);
    public IBrush ButtonTextBrush => new SolidColorBrush(Configuration.ButtonTextColor);
    public IBrush BackButtonForegroundBrush => new SolidColorBrush(Configuration.BackButtonForegroundColor);
    public IBrush SliderFillBrush => new SolidColorBrush(Configuration.SliderFillColor);
    public IBrush SliderTrackBrush => new SolidColorBrush(Configuration.SliderTrackColor);
    public IBrush SliderThumbBrush => new SolidColorBrush(Configuration.SliderThumbColor);
    public IBrush SliderThumbBorderBrush => new SolidColorBrush(Configuration.SliderThumbBorderColor);
    public IBrush CheckBoxBorderBrush => new SolidColorBrush(Configuration.CheckBoxBorderColor);
    public IBrush CheckBoxFillBrush => new SolidColorBrush(Configuration.CheckBoxFillColor);
    public IBrush CheckBoxCheckBrush => new SolidColorBrush(Configuration.CheckBoxCheckColor);
    [ObservableProperty] private double _bgmVolume;
    [ObservableProperty] private double _sfxVolume;
    [ObservableProperty] private double _voiceVolume;
    [ObservableProperty] private double _textSpeed;
    [ObservableProperty] private double _autoDelay;
    [ObservableProperty] private double _quickDelay;
    [ObservableProperty] private bool _fullscreen;
    public SettingsViewModel(ISettingsService settings, IGameScreenNavigator navigator, SettingsUiConfiguration configuration)
    {
        _settings = settings; _navigator = navigator; Configuration = configuration;
        var s = settings.GetSnapshot(); BgmVolume = s.BgmVolume; SfxVolume = s.SfxVolume; VoiceVolume = s.VoiceVolume; TextSpeed = s.TextSpeed; AutoDelay = s.AutoAdvanceInterval; QuickDelay = s.QuickAdvanceInterval; Fullscreen = s.Fullscreen;
    }
    partial void OnBgmVolumeChanged(double value) => _settings.BgmVolume = (float)value;
    partial void OnSfxVolumeChanged(double value) => _settings.SfxVolume = (float)value;
    partial void OnVoiceVolumeChanged(double value) => _settings.VoiceVolume = (float)value;
    partial void OnTextSpeedChanged(double value) => _settings.TextSpeed = (float)value;
    partial void OnFullscreenChanged(bool value) => _settings.Fullscreen = value;
    partial void OnAutoDelayChanged(double value) { var s = _settings.GetSnapshot(); s.AutoAdvanceInterval = (float)value; _settings.ApplySnapshot(s); }
    partial void OnQuickDelayChanged(double value) { var s = _settings.GetSnapshot(); s.QuickAdvanceInterval = (float)value; _settings.ApplySnapshot(s); }
    [RelayCommand] private Task BackAsync() => _navigator.GoBackAsync();
}
