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
    StopSEffect
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
    bool SupportsTrackSEffects = false)
{
    public static AudioBackendCapabilities None { get; } = new();
}

/// <summary>A voice-level request. Queue ownership always remains in AudioRuntime.</summary>
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
    JsonElement? SEffectParameters = null);

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
    AudioCompletion Completion = AudioCompletion.Discard);

public sealed record AudioStopRequest(string TrackId, bool ClearQueue, float FadeOutSeconds = 0f);

public sealed record AudioSEffectRequest(
    string TrackId,
    string InstanceId,
    string SEffectId,
    JsonElement Parameters);
