using GalNet.Contracts.Builtins.Audio;

namespace GalNet.Contracts.Builtins;

/// <summary>Optional three-group example selected explicitly by default project templates and samples.</summary>
public static class BuiltinAudioDefaults
{
    public static AudioConfiguration Create() => new(
    [
        new() { Id = "bgm", Name = "Settings.BgmVolume", GroupId = "bgm", PreserveDuringCatchUp = true, DefaultGain = .8f, MaxVoices = 1, MaxQueuedItems = 64, OverflowPolicy = AudioOverflowPolicy.StopOldest },
        new() { Id = "sfx", Name = "Settings.SfxVolume", GroupId = "sfx", MaxVoices = 8 },
        new() { Id = "vocal", Name = "Settings.VoiceVolume", GroupId = "vocal", MaxVoices = 1, MaxQueuedItems = 64, OverflowPolicy = AudioOverflowPolicy.StopOldest }
    ],
    [
        new() { Id = "bgm", Name = "Settings.BgmVolume" },
        new() { Id = "sfx", Name = "Settings.SfxVolume" },
        new() { Id = "vocal", Name = "Settings.VoiceVolume" }
    ]);
}
