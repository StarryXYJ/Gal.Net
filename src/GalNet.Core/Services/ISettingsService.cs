using GalNet.Core.Settings;

namespace GalNet.Core.Services;

/// <summary>
/// Game settings service — reads/writes player preferences (volume, text speed, fullscreen).
/// </summary>
public interface ISettingsService
{
    float BgmVolume { get; set; }
    float SfxVolume { get; set; }
    float VoiceVolume { get; set; }
    float TextSpeed { get; set; }
    bool Fullscreen { get; set; }

    /// <summary>Get current settings snapshot.</summary>
    GameSettings GetSnapshot();

    /// <summary>Restore from a snapshot.</summary>
    void ApplySnapshot(GameSettings settings);

    /// <summary>Load settings from disk.</summary>
    Task LoadAsync(string path);

    /// <summary>Persist settings to disk.</summary>
    Task SaveAsync(string path);

    /// <summary>Fired when any setting changes.</summary>
    event Action? Changed;
}
