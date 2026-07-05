using GalNet.Core.Services;

namespace GalNet.Control.ViewModels;

/// <summary>
/// 设置页 ViewModel —— 包装 ISettingsService，提供返回导航。
/// </summary>
public class SettingsViewModel
{
    private readonly ISettingsService _settings;
    private readonly INavigationService _nav;

    public float BgmVolume
    {
        get => _settings.BgmVolume;
        set => _settings.BgmVolume = value;
    }

    public float SfxVolume
    {
        get => _settings.SfxVolume;
        set => _settings.SfxVolume = value;
    }

    public float VoiceVolume
    {
        get => _settings.VoiceVolume;
        set => _settings.VoiceVolume = value;
    }

    public float TextSpeed
    {
        get => _settings.TextSpeed;
        set => _settings.TextSpeed = value;
    }

    public bool Fullscreen
    {
        get => _settings.Fullscreen;
        set => _settings.Fullscreen = value;
    }

    public SettingsViewModel(ISettingsService settings, INavigationService nav)
    {
        _settings = settings;
        _nav = nav;
    }

    public void Back() => _nav.GoBack();
}
