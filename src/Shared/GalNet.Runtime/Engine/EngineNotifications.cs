using GalNet.Core.Runtime;
using GalNet.Core.Primitives;
using GalNet.Runtime.Logging;

namespace GalNet.Runtime.Engine;

/// <summary>One ordered, reentrancy-safe notification queue, published outside the execution gate.</summary>
internal sealed class EngineNotifications
{
    private readonly object _sync = new();
    private readonly Queue<Action> _pending = new();
    private bool _publishing;
    public event Action<GameEngineStateChange>? StateChanged;
    public event Action<PlaybackMode>? PlaybackChanged;
    public event Action<GameSnapshot>? CheckpointCreated;

    public void State(GameEngineStateChange value) => Enqueue(StateChanged, value);
    public void Playback(PlaybackMode value) => Enqueue(PlaybackChanged, value);
    public void Checkpoint(GameSnapshot value) => Enqueue(CheckpointCreated, value, SnapshotCopy.Clone);

    private void Enqueue<T>(Action<T>? handlers, T value, Func<T, T>? copy = null)
    {
        if (handlers is null) return;
        lock (_sync) _pending.Enqueue(() =>
        {
            foreach (var handler in handlers.GetInvocationList())
                try { ((Action<T>)handler)(copy is null ? value : copy(value)); }
                catch (Exception error)
                {
                    try { GameLog.Logger.Warning(error, "Game notification observer failed"); }
                    catch (Exception) { /* Logging cannot strand the notification queue. */ }
                }
        });
    }

    public void Publish()
    {
        lock (_sync)
        {
            if (_publishing) return;
            _publishing = true;
        }
        while (true)
        {
            Action publish;
            lock (_sync)
                if (!_pending.TryDequeue(out publish!)) { _publishing = false; return; }
            publish();
        }
    }
}
