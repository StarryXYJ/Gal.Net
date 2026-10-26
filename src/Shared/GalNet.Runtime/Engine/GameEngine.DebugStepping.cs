using GalNet.Core.Graph;
using GalNet.Core.Primitives;

namespace GalNet.Runtime.Engine;

/// <summary>Exclusive primitive range supplied by the matching content's authoring map.</summary>
public sealed record GameDebugSpan(string GroupId, int Start, int End);

public sealed partial class GameEngine
{
    public sealed partial class DebugSession
    {
        private HashSet<(string Group, int Index)> _breakpoints = [];
        private (string Group, int Index)? _skipBreakpoint;
        private GameDebugSpan? _stepBoundary;
        private GameDebugSpan? _forcedTarget;

        private void ValidateSpan(GameDebugSpan span)
        {
            if (_engine._navigation.FindNode(span.GroupId) is not Group group || span.Start < 0 ||
                span.End < span.Start || span.End > group.Entries.Count)
                throw new ArgumentException("Invalid compiled author span.", nameof(span));
        }

        public async Task SetBreakpointsAsync(long generation, GameDebugSpan[] spans, CancellationToken token = default)
        {
            if (spans.Length > 4096) throw new ArgumentException("Too many breakpoints.", nameof(spans));
            await _engine._dispatchGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Check(generation);
                foreach (var span in spans) ValidateSpan(span);
                _breakpoints = spans.Select(span => (span.GroupId, span.Start)).ToHashSet();
            }
            finally { _engine._dispatchGate.Release(); }
        }

        internal bool BeforeNormalEntry(string group, int index)
        {
            if (_forcedTarget is { } forced && (group != forced.GroupId || index >= forced.End)) _forcedTarget = null;
            if (_stepBoundary is { } step && (group != step.GroupId || index >= step.End))
            { Paused = true; return true; }
            var position = (group, index);
            if (_skipBreakpoint == position) { _skipBreakpoint = null; return false; }
            if (!_breakpoints.Contains(position)) return false;
            Paused = true;
            _engine.SetPlaybackModeCore(PlaybackMode.Manual);
            _engine.CancelPlaybackTimer();
            return true;
        }

        internal bool ForceTargetCondition(string group, int index)
        {
            if (_forcedTarget is not { } span) return false;
            if (span.GroupId == group && index >= span.Start && index < span.End) return true;
            _forcedTarget = null;
            return false;
        }

        /// <summary>Runs within one author range, preserving ordinary blocking/skip and condition behavior.</summary>
        public async Task StepAsync(long generation, GameDebugSpan span, CancellationToken token = default)
        {
            await _engine._dispatchGate.WaitAsync(token).ConfigureAwait(false);
            var stepping = false;
            try
            {
                Check(generation);
                ValidateSpan(span);
                if (!Paused || Replaying || _engine._restorationPending || span.GroupId != _engine.CurrentNodeId ||
                    _engine.EntryIndex < span.Start || _engine.EntryIndex > span.End)
                    throw new InvalidOperationException("Pause at the selected author span before stepping.");
                _engine.ThrowFlowError();
                stepping = true;
                _stepBoundary = span;
                _skipBreakpoint = (_engine.CurrentNodeId, _engine.EntryIndex);
                Paused = false;
                _engine._started = true;
                _engine.IsRunning = !_engine._runtime.IsGameEnded;
                _engine.RemoveCompleted();
                if (_engine._pendingChoice is not null) return;
                if (_engine._active.EarliestBlocker() is { } blocker)
                    foreach (var item in _engine._active.SkipCandidates(blocker).ToArray()) item.Instance.Skip();
                _engine.RemoveCompleted();
                _engine._advanceBoundary = false;
                await _engine.ContinueUntilBlockedAsync(token).ConfigureAwait(false);
                _engine.ThrowFlowError();
                _engine.UpdateLastStableSnapshot();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (stepping)
            {
                _engine._flowError = error;
                _engine.IsRunning = false;
                throw;
            }
            finally
            {
                if (stepping) Paused = true;
                _stepBoundary = null;
                _engine.CaptureStateChange();
                _engine._dispatchGate.Release();
                _engine.PublishStateChanges();
            }
        }
    }
}
