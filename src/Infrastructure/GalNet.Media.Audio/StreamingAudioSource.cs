namespace GalNet.Media.Audio;

/// <summary>One producer, one consumer. No file I/O, waits, or allocations on Read.</summary>
public sealed class StreamingAudioSource : IAudioPcmSource, IAsyncDisposable
{
    private readonly float[] _ring = new float[PcmMixer.SampleRate * 2 * 2];
    private readonly CancellationTokenSource _cancel;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _producer;
    private long _read;
    private long _written;
    private int _finished;
    public double DurationSeconds { get; }
    public Exception? Error { get; private set; }
    public bool IsFinished => Volatile.Read(ref _finished) != 0 && Volatile.Read(ref _written) == Volatile.Read(ref _read);

    public StreamingAudioSource(IAudioPcmDecoder decoder, CancellationToken cancellationToken = default)
    {
        DurationSeconds = decoder.DurationSeconds;
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _producer = Task.Run(() => ProduceAsync(decoder), CancellationToken.None);
    }

    public Task PrepareAsync(CancellationToken token) => _ready.Task.WaitAsync(token);

    private async Task ProduceAsync(IAudioPcmDecoder decoder)
    {
        using (decoder)
        {
            var buffer = new float[4096];
            try
            {
                while (true)
                {
                    _cancel.Token.ThrowIfCancellationRequested();
                    var written = Volatile.Read(ref _written);
                    var free = _ring.Length - (int)(written - Volatile.Read(ref _read));
                    if (free < buffer.Length) { await Task.Delay(5, _cancel.Token).ConfigureAwait(false); continue; }
                    var count = decoder.Read(buffer);
                    if (count < 0 || count > buffer.Length || count % 2 != 0) throw new InvalidDataException("Decoder returned invalid stereo PCM.");
                    if (count == 0) break;
                    var offset = (int)(written % _ring.Length);
                    var first = Math.Min(count, _ring.Length - offset);
                    buffer.AsSpan(0, first).CopyTo(_ring.AsSpan(offset));
                    buffer.AsSpan(first, count - first).CopyTo(_ring);
                    Volatile.Write(ref _written, written + count);
                    _ready.TrySetResult();
                }
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { _ready.TrySetCanceled(_cancel.Token); }
            catch (Exception error) { Error = error; _ready.TrySetException(error); }
            finally { Volatile.Write(ref _finished, 1); _ready.TrySetResult(); }
        }
    }

    public int Read(Span<float> samples)
    {
        var read = Volatile.Read(ref _read);
        var count = (int)Math.Min(samples.Length, Volatile.Read(ref _written) - read);
        var offset = (int)(read % _ring.Length);
        var first = Math.Min(count, _ring.Length - offset);
        _ring.AsSpan(offset, first).CopyTo(samples);
        _ring.AsSpan(0, count - first).CopyTo(samples[first..]);
        Volatile.Write(ref _read, read + count);
        return count;
    }

    public async ValueTask DisposeAsync()
    {
        await _cancel.CancelAsync().ConfigureAwait(false);
        await _producer.ConfigureAwait(false);
        _cancel.Dispose();
    }
}
