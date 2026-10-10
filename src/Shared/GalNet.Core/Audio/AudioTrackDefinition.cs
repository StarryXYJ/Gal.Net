namespace GalNet.Core.Audio;

/// <summary>Fixed mixer routes exposed by the first audio feature.</summary>
public static class AudioBuses
{
    public const string Bgm = "bgm";
    public const string Sfx = "sfx";
    public const string Vocal = "vocal";

    public static bool IsValid(string? value) => value is Bgm or Sfx or Vocal;
}

public enum AudioOverflowPolicy
{
    DropNewest,
    StopOldest
}

public enum AudioQueueOverflowPolicy
{
    DropNewest,
    DropOldest
}

/// <summary>Author-configured logical route, independent from backend players.</summary>
public sealed class AudioTrackDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Bus { get; set; } = AudioBuses.Sfx;
    public float DefaultGain { get; set; } = 1f;
    public int MaxVoices { get; set; } = 1;
    public int MaxQueuedItems { get; set; }
    public AudioOverflowPolicy OverflowPolicy { get; set; } = AudioOverflowPolicy.DropNewest;
    public AudioQueueOverflowPolicy QueueOverflowPolicy { get; set; } = AudioQueueOverflowPolicy.DropNewest;

    public static List<AudioTrackDefinition> CreateDefaults() =>
    [
        new() { Id = AudioBuses.Bgm, Name = "背景音乐", Bus = AudioBuses.Bgm, DefaultGain = .8f, MaxVoices = 1, MaxQueuedItems = 64, OverflowPolicy = AudioOverflowPolicy.StopOldest },
        new() { Id = AudioBuses.Sfx, Name = "音效", Bus = AudioBuses.Sfx, DefaultGain = 1f, MaxVoices = 8, MaxQueuedItems = 0, OverflowPolicy = AudioOverflowPolicy.DropNewest },
        new() { Id = AudioBuses.Vocal, Name = "语音", Bus = AudioBuses.Vocal, DefaultGain = 1f, MaxVoices = 1, MaxQueuedItems = 64, OverflowPolicy = AudioOverflowPolicy.StopOldest }
    ];
}
