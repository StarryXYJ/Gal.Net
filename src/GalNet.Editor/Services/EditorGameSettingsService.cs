using GalNet.Core.Services;
using GalNet.Core.Settings;

namespace GalNet.Editor.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly GameSettings _settings = new();
    public event Action? Changed;
    public float BgmVolume { get => _settings.BgmVolume; set { _settings.BgmVolume = value; Changed?.Invoke(); } }
    public float SfxVolume { get => _settings.SfxVolume; set { _settings.SfxVolume = value; Changed?.Invoke(); } }
    public float VoiceVolume { get => _settings.VoiceVolume; set { _settings.VoiceVolume = value; Changed?.Invoke(); } }
    public float TextSpeed { get => _settings.TextSpeed; set { _settings.TextSpeed = value; Changed?.Invoke(); } }
    public bool Fullscreen { get => _settings.Fullscreen; set { _settings.Fullscreen = value; Changed?.Invoke(); } }
    public GameSettings GetSnapshot() => new()
    {
        BgmVolume = BgmVolume, SfxVolume = SfxVolume, VoiceVolume = VoiceVolume,
        TextSpeed = TextSpeed, Fullscreen = Fullscreen
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
    public Task LoadAsync(string path) => Task.CompletedTask;
    public Task SaveAsync(string path) => Task.CompletedTask;
}
