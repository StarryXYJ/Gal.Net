namespace GalNet.Runtime.Audio;

/// <summary>
/// 音频通道状态。
/// </summary>
public enum AudioState { Stopped, Playing, Paused }

/// <summary>
/// 音频通道定义。
/// </summary>
public sealed class AudioChannel
{
    public string Name { get; init; } = "";
    public float Volume { get; set; } = 1f;
    public string? CurrentAsset { get; set; }
    public AudioState State { get; set; } = AudioState.Stopped;
    public int RemainingTimes { get; set; }
}

/// <summary>
/// 音频通道管理器 —— 管理所有音频通道的生命周期。
/// </summary>
public sealed class AudioChannelManager
{
    private readonly Dictionary<string, AudioChannel> _channels = new();

    /// <summary>初始化默认通道。</summary>
    public void Initialize(int sfxChannelCount = 4)
    {
        _channels.Clear();
        AddChannel("bgm");
        AddChannel("bgm2");
        AddChannel("voice");
        for (var i = 1; i <= sfxChannelCount; i++)
            AddChannel($"sfx{i}");
    }

    private void AddChannel(string name)
    {
        _channels[name] = new AudioChannel { Name = name, Volume = 1f };
    }

    /// <summary>获取指定通道，不存在返回 null。</summary>
    public AudioChannel? GetChannel(string name)
    {
        return _channels.TryGetValue(name, out var ch) ? ch : null;
    }

    /// <summary>获取所有通道。</summary>
    public IReadOnlyCollection<AudioChannel> AllChannels => _channels.Values;

    /// <summary>播放音频。</summary>
    public void Play(string channel, string asset, float volume, string mode, int times)
    {
        if (!_channels.TryGetValue(channel, out var ch)) return;

        ch.CurrentAsset = asset;
        ch.Volume = volume;
        ch.State = AudioState.Playing;
        ch.RemainingTimes = mode == "repeat" ? int.MaxValue : times;
    }

    /// <summary>停止音频。</summary>
    public void Stop(string channel)
    {
        if (!_channels.TryGetValue(channel, out var ch)) return;
        ch.State = AudioState.Stopped;
        ch.CurrentAsset = null;
        ch.RemainingTimes = 0;
    }

    /// <summary>暂停。</summary>
    public void Pause(string channel)
    {
        if (!_channels.TryGetValue(channel, out var ch)) return;
        if (ch.State == AudioState.Playing)
            ch.State = AudioState.Paused;
    }

    /// <summary>恢复。</summary>
    public void Resume(string channel)
    {
        if (!_channels.TryGetValue(channel, out var ch)) return;
        if (ch.State == AudioState.Paused)
            ch.State = AudioState.Playing;
    }

    /// <summary>停止所有。</summary>
    public void StopAll()
    {
        foreach (var ch in _channels.Values)
        {
            ch.State = AudioState.Stopped;
            ch.CurrentAsset = null;
        }
    }
}
