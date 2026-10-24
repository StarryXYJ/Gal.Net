using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed partial class SceneTargetAnalysis
{
    private static readonly Lazy<IEntryCatalog> TransitionCatalog = new(() => new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
    // This analysis owns an immutable snapshot; both successful and invalid expansions are cached per generation.
    private readonly Dictionary<EditorEntryData, EditorEntryData?> _transitionTimelines = new();
    private EditorEntryData TransitionTimeline(EditorEntryData entry)
    {
        if (_transitionTimelines.TryGetValue(entry, out var cached)) return cached ?? throw new InvalidDataException("Invalid transition.");
        _transitionTimelines[entry] = null;
        var definition = TransitionCatalog.Value.Get(entry.Type);
        var source = new GroupDocument
        {
            Entries = [new() { Id = entry.StableId, Type = entry.Type,
                // Unknown author extensions remain on the original document, not in the strict compiler input.
                Parameters = entry.Parameters.Where(pair => definition.Parameters.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) }]
        };
        var expanded = GalgroupCompiler.Compile(source, TransitionCatalog.Value).Document.Entries.Single();
        if (expanded.TypeId != BuiltinEntryIds.PlayTimeline) throw new InvalidDataException("Unsupported transition expansion.");
        var result = new EditorEntryData
        {
            StableId = entry.StableId,
            Type = expanded.TypeId,
            Parameters = expanded.Arguments.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
        };
        _transitionTimelines[entry] = result;
        return result;
    }
    private void TransferTransition(State state, string group, EditorEntryData entry)
    {
        var candidate = state.Clone();
        // Timeline queries recover from individual event errors. A composite must not publish only
        // the successful half of its expansion. Track local validity separately from upstream uncertainty.
        candidate.Complete = true;
        TransferTimeline(candidate, group, TransitionTimeline(entry));
        if (!candidate.Complete) throw new InvalidDataException("Incomplete transition expansion.");
        var slot = SceneAuthoringIdentities.CreationSlot(entry.Type)!;
        var handle = entry.Parameters[slot].GetString()!;
        if (candidate.Targets.TryGetValue(handle, out var target) && target.Locator.GroupId == group && target.Locator.EntryId == entry.StableId)
            candidate.Targets[handle] = target with { Locator = new(group, entry.StableId, slot), CreatedAtFrame = null, IsFuture = false };
        state.Targets.Clear();
        foreach (var pair in candidate.Targets) state.Targets.Add(pair.Key, pair.Value);
        state.Playbacks.Clear();
        foreach (var pair in candidate.Playbacks) state.Playbacks.Add(pair.Key, pair.Value);
    }
}
