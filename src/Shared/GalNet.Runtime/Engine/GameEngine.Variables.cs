using GalNet.Core.Runtime;
using GalNet.Core.Variable;

namespace GalNet.Runtime.Engine;

public sealed partial class GameEngine
{
    /// <summary>
    /// Applies explicit player./save. values under the execution gate. A restore or stop invalidates queued edits.
    /// Save values are also merged into the stable snapshot without moving its cursor or capturing pending presentation.
    /// Player persistence remains the host's responsibility.
    /// </summary>
    public Task SetVariablesAsync(IReadOnlyDictionary<string, object> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) return Task.CompletedTask;
        var copied = new Dictionary<string, object>(values);
        return UpdateVariablesAsync(_ => copied, cancellationToken);
    }

    /// <summary>Computes and validates a batch against independent, explicitly scoped snapshots under the execution gate.</summary>
    public async Task UpdateVariablesAsync(
        Func<IReadOnlyDictionary<string, Variable>, IReadOnlyDictionary<string, object>> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ThrowIfDisposed();
        var scope = _scopeCancellation.Token;
        await CancelCatchUpAndWaitAsync(cancellationToken).ConfigureAwait(false);
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            scope.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowFlowError();
            if (_restorationPending || Volatile.Read(ref _stopRequested) != 0)
                throw new InvalidOperationException("The run is restoring or stopped.");
            _runtime.State.Update(() =>
            {
                var current = _runtime.GetVariables(VariableScope.Player).ToDictionary(pair => "player." + pair.Key, pair => pair.Value);
                foreach (var pair in _runtime.GetVariables(VariableScope.Save)) current.Add("save." + pair.Key, pair.Value);
                var edits = update(current).ToArray();
                Validate(edits);
                if (edits.Length == 0) return;
                foreach (var (name, value) in edits) _runtime.SetVariable(name, value);
                var snapshot = SnapshotCopy.Clone(_lastStableSnapshot);
                var saved = _runtime.GetVariables(VariableScope.Save);
                foreach (var (name, _) in edits)
                    if (name.StartsWith("save.", StringComparison.Ordinal))
                        snapshot.Variables[name[5..]] = saved[name[5..]].Clone();
                _lastStableSnapshot = snapshot;
                _notifications.Checkpoint(snapshot);
            });
        }
        finally { FinishDispatch(); }
    }

    private static void Validate(KeyValuePair<string, object>[] edits)
    {
        foreach (var (name, value) in edits)
        {
            if ((!name.StartsWith("player.", StringComparison.Ordinal) && !name.StartsWith("save.", StringComparison.Ordinal)) ||
                string.IsNullOrWhiteSpace(name[(name.IndexOf('.') + 1)..]))
                throw new ArgumentException("External variable names require an explicit player. or save. scope.", nameof(edits));
            if (value is not (bool or int or string) &&
                !(value is long integer && integer is >= int.MinValue and <= int.MaxValue) &&
                !(value is float number && float.IsFinite(number)) &&
                !(value is double real && float.IsFinite((float)real)))
                throw new ArgumentException($"Variable '{name}' requires bool, Int32, finite Single or string values.", nameof(edits));
        }
    }
}
