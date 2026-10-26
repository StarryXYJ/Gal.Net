using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed partial class SceneTargetAnalysis
{
    // Checkpoints belong to this immutable analysis only. A new generation never retains the old analysis.
    private const int CheckpointLimit = 128;
    private readonly Dictionary<(string Group, int Index), State> _checkpoints = [];
    public int EntryTransferCount { get; private set; }
    public int ReusedGroupCount { get; private set; }
    public int CheckpointCount => _checkpoints.Count;

    private State TransferGroup(State input, string group, List<EditorEntryData> entries, SceneTargetAnalysis? previous)
    {
        foreach (var key in _checkpoints.Keys.Where(key => key.Group == group).ToArray()) _checkpoints.Remove(key);
        if (previous is { _budgetExceeded: false } && previous._inputs.TryGetValue(group, out var oldInput) && Equal(input, oldInput)
            && previous._groups.TryGetValue(group, out var oldEntries))
        {
            var common = 0;
            while (common < Math.Min(entries.Count, oldEntries.Count) && SameEntry(entries[common], oldEntries[common])) common++;
            foreach (var pair in previous._checkpoints.Where(pair => pair.Key.Group == group && pair.Key.Index <= common)) Remember(pair.Key, pair.Value);
            if (common == entries.Count && common == oldEntries.Count && previous._outputs.TryGetValue(group, out var output))
            { ReusedGroupCount++; return output.Clone(); }
        }
        return Prefix(input, group, entries, entries.Count);
    }

    private State Prefix(State input, string group, List<EditorEntryData> entries, int end)
    {
        var start = _checkpoints.Keys.Where(key => key.Group == group && key.Index <= end).Select(key => key.Index).DefaultIfEmpty(0).Max();
        var state = start == 0 ? input.Clone() : _checkpoints[(group, start)].Clone();
        for (var index = start; index < end; index++)
        {
            state = Apply(state, group, entries[index]);
            if ((index + 1) % 64 == 0) Remember((group, index + 1), state);
        }
        Remember((group, end), state);
        return state.Clone();
    }

    private void Remember((string Group, int Index) key, State state)
    {
        if (key.Index == 0) return;
        if (!_checkpoints.ContainsKey(key) && _checkpoints.Count >= CheckpointLimit) _checkpoints.Remove(_checkpoints.Keys.First());
        _checkpoints[key] = state.Clone();
    }

    public static bool SameEntry(EditorEntryData? left, EditorEntryData? right) => ReferenceEquals(left, right)
        || left is not null && right is not null
        && left.StableId == right.StableId && left.Type == right.Type && left.Condition == right.Condition
        && left.Parameters.Count == right.Parameters.Count && left.Parameters.All(pair => right.Parameters.TryGetValue(pair.Key, out var value) && JsonElement.DeepEquals(pair.Value, value));

    public static bool Equivalent(SceneTargetResult left, SceneTargetResult right) => left.Complete == right.Complete && left.Reachable == right.Reachable
        && EqualDomain(left.Targets.ToDictionary(target => target.Handle), right.Targets.ToDictionary(target => target.Handle))
        && EqualDomain(left.Playbacks.ToDictionary(target => target.Handle), right.Playbacks.ToDictionary(target => target.Handle));
}
