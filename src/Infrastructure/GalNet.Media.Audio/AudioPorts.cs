namespace GalNet.Media.Audio;

public delegate void AudioRenderCallback(Span<float> output);

/// <summary>Platform output pulls interleaved float32 stereo at 48 kHz.</summary>
public interface IAudioOutput : IDisposable
{
    void Start(AudioRenderCallback render);
}

/// <summary>Blocking decoder. Called only by the source's background producer.</summary>
public interface IAudioPcmDecoder : IDisposable
{
    double DurationSeconds { get; }
    int Read(Span<float> samples);
}

public interface IAudioPcmSource
{
    double DurationSeconds { get; }
    bool IsFinished { get; }
    int Read(Span<float> samples);
}
