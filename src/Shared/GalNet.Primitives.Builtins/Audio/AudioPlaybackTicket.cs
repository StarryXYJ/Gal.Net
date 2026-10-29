namespace GalNet.Primitives.Builtins.Audio;

/// <summary>One logical playback, including time spent queued. Continuations never run under the audio lock.</summary>
public sealed class AudioPlaybackTicket
{
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<AudioPlaybackTicket> _stop;
    internal AudioPlaybackTicket(Action<AudioPlaybackTicket> stop) => _stop = stop;
    public Task Completion => _completed.Task;
    public bool IsCompleted => _completed.Task.IsCompleted;
    public void Stop() => _stop(this);
    internal void Complete() => _completed.TrySetResult();
}
