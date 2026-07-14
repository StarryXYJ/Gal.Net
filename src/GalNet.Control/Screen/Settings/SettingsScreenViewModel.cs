using GalNet.Core.Services;

namespace GalNet.Control.Screen.BuiltIn;

/// <summary>
/// ViewModel for SettingsScreenView — bound to ISettingsService.
/// </summary>
public sealed class SettingsScreenViewModel
{
    private readonly ISettingsService _settings;
    private readonly INavigationHost _nav;

    public float BgmVolume
    {
        get => _settings.BgmVolume * 100;
        set => _settings.BgmVolume = value / 100f;
    }

    public float SfxVolume
    {
        get => _settings.SfxVolume * 100;
        set => _settings.SfxVolume = value / 100f;
    }

    public float VoiceVolume
    {
        get => _settings.VoiceVolume * 100;
        set => _settings.VoiceVolume = value / 100f;
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

    public SettingsScreenViewModel(ISettingsService settings, INavigationHost nav)
    {
        _settings = settings;
        _nav = nav;
    }

    public void Back() => _nav.CloseModal();
}
