using GalNet.Core.Services;
using GalNet.Core.Settings;
using Serilog;

namespace GalNet.Control.Services;

/// <summary>
/// Default implementation of ISettingsService.
/// Backed by a GameSettings instance, supports persistence.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly GameSettings _settings;

    public event Action? Changed;

    public SettingsService(GameSettings? settings = null)
    {
        _settings = settings ?? new GameSettings();
        Log.Debug("SettingsService created: TextSpeed={TextSpeed}", _settings.TextSpeed);
    }

    public float BgmVolume
    {
        get => _settings.BgmVolume;
        set { _settings.BgmVolume = value; Changed?.Invoke(); }
    }

    public float SfxVolume
    {
        get => _settings.SfxVolume;
        set { _settings.SfxVolume = value; Changed?.Invoke(); }
    }

    public float VoiceVolume
    {
        get => _settings.VoiceVolume;
        set { _settings.VoiceVolume = value; Changed?.Invoke(); }
    }

    public float TextSpeed
    {
        get => _settings.TextSpeed;
        set { _settings.TextSpeed = value; Changed?.Invoke(); }
    }

    public bool Fullscreen
    {
        get => _settings.Fullscreen;
        set { _settings.Fullscreen = value; Changed?.Invoke(); }
    }

    public GameSettings GetSnapshot() => new()
    {
        BgmVolume = _settings.BgmVolume,
        SfxVolume = _settings.SfxVolume,
        VoiceVolume = _settings.VoiceVolume,
        TextSpeed = _settings.TextSpeed,
        Fullscreen = _settings.Fullscreen,
    };

    public void ApplySnapshot(GameSettings settings)
    {
        _settings.BgmVolume = settings.BgmVolume;
        _settings.SfxVolume = settings.SfxVolume;
        _settings.VoiceVolume = settings.VoiceVolume;
        _settings.TextSpeed = settings.TextSpeed;
        _settings.Fullscreen = settings.Fullscreen;
        Changed?.Invoke();
    }

    public async Task LoadAsync(string path)
    {
        // Future: JSON deserialize
        await Task.CompletedTask;
    }

    public async Task SaveAsync(string path)
    {
        // Future: JSON serialize
        await Task.CompletedTask;
    }
}
