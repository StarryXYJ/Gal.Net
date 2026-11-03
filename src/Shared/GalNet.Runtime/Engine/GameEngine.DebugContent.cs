using GalNet.Core.Graph;

namespace GalNet.Runtime.Engine;

public sealed record GameDebugContentBoundary(string NodeId, int EntryIndex, string[] ActiveEntryIds);
public sealed record GameDebugContentReplacement(Graph Graph, GameDebugContentBoundary Expected, int CandidateEntryIndex);

public sealed partial class GameEngine
{
    public sealed partial class DebugSession
    {
        /// <summary>Atomically adopts a verified navigation snapshot at an Editor-mapped execution boundary.</summary>
        public async Task<long> ReplaceContentAsync(long generation, GameDebugContentReplacement replacement,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            ArgumentNullException.ThrowIfNull(replacement.Graph);
            ArgumentNullException.ThrowIfNull(replacement.Expected);
            ArgumentNullException.ThrowIfNull(replacement.Expected.ActiveEntryIds);
            if (replacement.Expected.EntryIndex < 0 || replacement.CandidateEntryIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(replacement));

            var navigation = new GameGraphNavigator(replacement.Graph);
            var missing = navigation.Nodes.OfType<Group>().SelectMany(group => group.Entries)
                .Where(entry => !_engine._view.CanExecute(entry.Type))
                .Select(entry => entry.Type).Distinct(StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"Game instructions have no implementation: {string.Join(", ", missing)}.");

            CancelSeek();
            await PendingSeek.WaitAsync(cancellationToken).ConfigureAwait(false);
            await _engine.CancelCatchUpAndWaitAsync(cancellationToken).ConfigureAwait(false);
            await _engine._dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Check(generation);
                ValidateBoundary(replacement.Expected);
                var candidate = navigation.FindNode(replacement.Expected.NodeId)
                    ?? throw new InvalidOperationException($"Content revision does not contain current node '{replacement.Expected.NodeId}'.");
                if (candidate is Group group)
                {
                    if (replacement.CandidateEntryIndex > group.Entries.Count)
                        throw new InvalidOperationException("The mapped candidate Entry position is invalid.");
                }
                else if (replacement.CandidateEntryIndex != 0)
                    throw new InvalidOperationException("A non-Group node must use Entry position zero.");

                _engine._navigation = navigation;
                _engine._runtime.SetEntryIndex(replacement.CandidateEntryIndex);
                OnContentReplaced();
                var nextGeneration = Interlocked.Increment(ref _generation);
                _engine.UpdateLastStableSnapshot();
                return nextGeneration;
            }
            finally { _engine._dispatchGate.Release(); _engine.PublishStateChanges(); }
        }

        private void ValidateBoundary(GameDebugContentBoundary expected)
        {
            var active = _engine._active.Where(item => !item.Instance.IsCompleted)
                .Select(item => item.StableId).Order(StringComparer.Ordinal).ToArray();
            if (!string.Equals(expected.NodeId, _engine.CurrentNodeId, StringComparison.Ordinal) ||
                expected.EntryIndex != _engine.EntryIndex ||
                !active.SequenceEqual(expected.ActiveEntryIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidOperationException("The debug execution boundary has changed.");
        }
    }
}
