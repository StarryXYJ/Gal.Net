using System.Globalization;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Variable;

namespace GalNet.Runtime.Engine;

public sealed record GameDebugVariable(VariableScope Scope, string Uid, string Name, VariableType Type, string Value);
public sealed record GameDebugGroup(string Id, string[] Entries);
public sealed record GameDebugChoice(string Id, string[] Options);
public sealed record GameDebugState(long Generation, GameEngineState State, bool Paused, string NodeId, int EntryIndex,
    string[] ActiveEntryIds, GameDebugVariable[] Variables, GameDebugGroup[] Groups, string[] CachedGroups, string? Error, GameDebugChoice? Choice);

public sealed partial class GameEngine
{
    private DebugSession? _debugger;

    /// <summary>Explicit opt-in. The caller owns the binding; no network or platform services are created.</summary>
    public DebugSession AttachDebugging(GameDebugOptions? options = null)
    {
        _dispatchGate.Wait();
        try
        {
            ThrowIfDisposed();
            if (_debugger is not null) throw new InvalidOperationException("A debugger is already attached.");
            return _debugger = new DebugSession(this, options ?? new());
        }
        finally { _dispatchGate.Release(); }
    }

    /// <summary>Optional, borrowed controls. All state is copied or modified under the existing execution gate.</summary>
    public sealed partial class DebugSession : IDisposable
    {
        private readonly GameEngine _engine;
        private bool _disposed;
        private long _generation = 1;
        internal bool Paused { get; private set; }
        public long Generation => Interlocked.Read(ref _generation);
        internal DebugSession(GameEngine engine, GameDebugOptions options)
        {
            _engine = engine;
            _options = new GameDebugOptions
            {
                Instructions = options.Instructions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                SynchronizePresentation = options.SynchronizePresentation
            };
            _initialScene = CaptureScene();
        }

        public async Task<GameDebugState> InspectAsync(CancellationToken cancellationToken = default)
        {
            await _engine._dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Check();
                var variables = Enum.GetValues<VariableScope>().SelectMany(scope =>
                    _engine._runtime.GetVariables(scope).Values.Select(variable =>
                        new GameDebugVariable(scope, variable.Uid, variable.Name, variable.Type, variable.Value.Serialize()))).ToArray();
                return new(Generation, _engine.State, Paused, _engine.CurrentNodeId, _engine.EntryIndex,
                    _engine._active.Where(item => !item.Instance.IsCompleted).OrderBy(item => item.Sequence).Select(item => item.StableId).ToArray(), variables,
                    _engine._navigation.Nodes.OfType<Group>().Select(group => new GameDebugGroup(group.Id,
                        group.Entries.OfType<GalNet.Core.Entry.PrimitiveEntry>().Select(entry => entry.StableId).ToArray())).ToArray(),
                    _entrances.Keys.ToArray(), _engine._flowError?.ToString(),
                    _engine._pendingChoice is { } choice ? new(choice.Id, choice.Options.Select(item => item.Text).ToArray()) : null);
            }
            finally { _engine._dispatchGate.Release(); }
        }

        public async Task SetVariableAsync(long generation, GameDebugVariable expected, string value, CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(expected.Scope)) throw new ArgumentOutOfRangeException(nameof(expected));
            var parsed = ParseValue(expected.Type, value);
            await _engine._dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Check(generation);
                if (!_engine._runtime.GetVariables(expected.Scope).TryGetValue(expected.Name, out var current) ||
                    current.Uid != expected.Uid || current.Type != expected.Type || current.Value.Serialize() != expected.Value)
                    throw new InvalidOperationException("The variable has changed. Refresh it before applying the draft.");
                _engine._runtime.SetVariable((expected.Scope == VariableScope.Player ? "player." : "save.") + expected.Name, parsed);
                _engine.UpdateLastStableSnapshot();
            }
            finally { _engine._dispatchGate.Release(); }
        }

        public async Task PauseAsync(long generation, CancellationToken cancellationToken = default)
        {
            await _engine.CancelCatchUpAndWaitAsync(cancellationToken).ConfigureAwait(false);
            await _engine._dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Check(generation);
                Paused = true;
                _engine.SetPlaybackModeCore(PlaybackMode.Manual);
                _engine.CancelPlaybackTimer();
            }
            finally { _engine._dispatchGate.Release(); _engine.PublishStateChanges(); }
        }

        public async Task ResumeAsync(long generation, CancellationToken cancellationToken = default)
        {
            await _engine._dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Check(generation);
                Paused = false;
                _engine.ThrowFlowError();
                if (_engine._pendingChoice is { DebugSelection: { } selected } pending)
                {
                    pending.DebugSelection = null;
                    _engine.StartBackground(() => _engine.CompleteChoiceAsync(pending, Task.FromResult(selected)));
                }
                if (_engine.State is GameEngineState.Ready)
                {
                    _engine._started = true;
                    _engine.IsRunning = true;
                }
                if (_engine.IsRunning && !_engine._restorationPending) _engine.ContinueUntilBlocked();
            }
            finally { _engine.CaptureStateChange(); _engine._dispatchGate.Release(); _engine.PublishStateChanges(); }
        }

        public async Task ChooseAsync(long generation, string choiceId, int visibleIndex, CancellationToken cancellationToken = default)
        {
            PendingChoice pending;
            await _engine._dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Check(generation);
                pending = _engine._pendingChoice ?? throw new InvalidOperationException("No active choice.");
                if (pending.Id != choiceId) throw new InvalidOperationException("The choice has changed.");
                if ((uint)visibleIndex >= pending.Options.Count) throw new ArgumentOutOfRangeException(nameof(visibleIndex));
            }
            finally { _engine._dispatchGate.Release(); }
            pending.Cancellation.Cancel();
            await _engine.CompleteChoiceAsync(pending, Task.FromResult(visibleIndex)).ConfigureAwait(false);
        }

        private void Check(long? generation = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _engine.ThrowIfDisposed();
            if (!ReferenceEquals(_engine._debugger, this) || (generation is not null && Generation != generation))
                throw new InvalidOperationException("The debug execution has changed. Refresh before submitting.");
            if (Volatile.Read(ref _engine._stopRequested) != 0) throw new InvalidOperationException("The run has stopped.");
        }
        private static object ParseValue(VariableType type, string text) => type switch
        {
            VariableType.Bool => bool.Parse(text),
            VariableType.Int => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            VariableType.Float => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
                ? value : throw new FormatException("A finite Float value is required."),
            VariableType.String => text,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        public void Dispose()
        {
            CancelSeek();
            _engine._dispatchGate.Wait();
            try
            {
                _disposed = true;
                if (ReferenceEquals(_engine._debugger, this)) _engine._debugger = null;
            }
            finally { _engine._dispatchGate.Release(); }
        }
    }
}
