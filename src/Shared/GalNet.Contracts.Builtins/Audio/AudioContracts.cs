using System.Text.Json;

namespace GalNet.Contracts.Builtins.Audio;

public enum AudioPlacement
{
    Enqueue,
    Next,
    Replace,
    ReplaceAll,
    ReplaceIfDifferent
}

public enum AudioCompletion
{
    Discard,
    RequeueTail
}

public enum AudioBackendAction
{
    Start,
    Stop,
    Pause,
    Resume,
    SetGain,
    ApplySEffect,
    StopSEffect,
    Configure,
    SetMix,
    ResetMixer,
    SetLevel
}

public enum AudioBackendEventKind
{
    Started,
    Ended,
    Failed
}

public sealed record AudioBackendCapabilities(
    bool SupportsConcurrentVoices = false,
    bool SupportsFade = false,
    bool SupportsTrackSEffects = false,
    bool SupportsMixer = false)
{
    public static AudioBackendCapabilities None { get; } = new();
}

/// <summary>A voice or mixer command. TrackId identifies the scoped target for target-level actions; queue ownership remains in AudioRuntime.</summary>
public sealed record AudioBackendRequest(
    AudioBackendAction Action,
    string TrackId,
    string VoiceId,
    long Generation,
    string? AssetId = null,
    float Gain = 1f,
    float FadeSeconds = 0f,
    string? SEffectId = null,
    string? SEffectInstanceId = null,
    JsonElement? SEffectParameters = null,
    SEffectScope Scope = SEffectScope.Track,
    AudioConfiguration? Configuration = null,
    AudioMixOptions? Mix = null,
    float FadeOutSeconds = 0,
    bool Muted = false);

public sealed record AudioBackendEvent(
    AudioBackendEventKind Kind,
    string TrackId,
    string VoiceId,
    long Generation,
    string? Message = null);

public sealed record AudioPlayRequest(
    string TrackId,
    string AssetId,
    AudioPlacement Placement,
    float Gain = 1f,
    float FadeInSeconds = 0f,
    int LoopCount = 1,
    AudioCompletion Completion = AudioCompletion.Discard,
    float FadeOutSeconds = 0f);

public sealed record AudioStopRequest(string TrackId, bool ClearQueue, float? FadeOutSeconds = null);

public sealed record AudioSEffectRequest(
    string TargetId,
    string InstanceId,
    string SEffectId,
    JsonElement Parameters,
    SEffectScope Scope = SEffectScope.Track);
