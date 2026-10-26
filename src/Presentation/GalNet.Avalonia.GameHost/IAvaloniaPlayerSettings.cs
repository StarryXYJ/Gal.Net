using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Execution;
using GalNet.Runtime.Variables;

namespace GalNet.Avalonia.GameHost;

/// <summary>Projects own their preference model and UI; the default host consumes these module inputs.</summary>
public interface IAvaloniaPlayerSettings : IPlayerSettingsService
{
    PlaybackOptions PlaybackOptions { get; }
    AudioMixOptions AudioMix { get; }
    double TextSpeed { get; }
    event Action? SettingsChanged;
    Task AttachAsync(PlayerVariableService variables, AudioConfiguration audio, bool showTracks,
        CancellationToken cancellationToken = default);
    Task FlushAsync();
    void BindWindow(global::Avalonia.Controls.Window? window) { }
    Task RestoreDisplayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Optional project onboarding, independent of the default language-selection implementation.</summary>
public interface IPlayerStartup
{
    Task<bool> InitializeAsync(PlayerVariableService variables, string defaultCulture,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken = default);
}
