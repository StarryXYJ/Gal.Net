using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed record SceneAuthorAddress(string GroupId, string EntryId);
public sealed record SceneIdentityDefinition(SceneAuthorAddress Owner, string JsonPointer, string Handle, SceneOutputSlot Slot, string? EventId)
{ public string? Qualifier { get; init; } }
public sealed record SceneIdentityReference(SceneAuthorAddress Owner, string JsonPointer, string Handle, SceneReferenceSlot Slot, SceneTimelinePosition? Position, string? EventId)
{ public string? Qualifier { get; init; } }
public sealed record SceneReferenceDiagnostic(string Code, SceneAuthorAddress Owner, string JsonPointer, string? Handle = null, IReadOnlyList<SceneIdentityDefinition>? Candidates = null, long? Revision = null);

/// <summary>Syntactic relationships include unreachable and unopened Groups. They do not prove a unique creator.</summary>
public sealed class SceneReferenceIndex
{
    public IReadOnlyList<SceneIdentityDefinition> Definitions { get; }
    public IReadOnlyList<SceneIdentityReference> References { get; }
    public IReadOnlyList<SceneReferenceDiagnostic> Coverage { get; }
    public bool Complete => Coverage.Count == 0;
    public SceneReferenceIndex(IReadOnlyDictionary<string, List<EditorEntryData>> groups)
    {
        var definitions = new List<SceneIdentityDefinition>(); var references = new List<SceneIdentityReference>(); var coverage = new List<SceneReferenceDiagnostic>();
        var remaining = 10000;
        foreach (var (groupId, entries) in groups)
            foreach (var entry in entries)
            {
                var owner = new SceneAuthorAddress(groupId, entry.StableId);
                if (--remaining < 0) { coverage.Add(new("scene.analysis.limit", owner, "/parameters")); goto Finished; }
                var root = JsonSerializer.SerializeToNode(entry.Parameters)!.AsObject();
                try
                {
                    SceneIdentityWalker.Walk(entry.Type, root, slot =>
                {
                    if (--remaining < 0) throw new IndexBudgetExceededException();
                    if (string.IsNullOrWhiteSpace(slot.Handle)) return;
                    if (slot.Output is { } output) definitions.Add(new(owner, slot.Pointer, slot.Handle, output, slot.EventId) { Qualifier = slot.Qualifier });
                    if (slot.Reference is { } reference) references.Add(new(owner, slot.Pointer, slot.Handle, reference, slot.Position, slot.EventId) { Qualifier = slot.Qualifier });
                }, gap => { if (--remaining < 0) throw new IndexBudgetExceededException(); coverage.Add(new(gap.Code, owner, gap.Pointer)); }, step: () => { if (--remaining < 0) throw new IndexBudgetExceededException(); });
                }
                catch (IndexBudgetExceededException) { coverage.Add(new("scene.analysis.limit", owner, "/parameters")); goto Finished; }
            }
    Finished:
        Definitions = definitions.AsReadOnly(); References = references.AsReadOnly(); Coverage = coverage.Distinct().ToArray();
    }
    public IReadOnlyList<SceneIdentityDefinition> Candidates(SceneIdentityReference reference) => Definitions.Where(definition =>
        definition.Handle == reference.Handle && definition.Slot.Domain == reference.Slot.Domain && definition.Qualifier == reference.Qualifier && reference.Slot.Kinds.Contains(definition.Slot.Kind)).ToArray();
    private sealed class IndexBudgetExceededException : Exception { }
}

public sealed record SceneReferenceState(string Code, bool Complete, bool Reachable, IReadOnlyList<SceneTarget> Targets)
{ public IReadOnlyList<AudioEffectTarget> AudioEffects { get; init; } = []; }
public sealed record SceneReferenceChange(SceneIdentityReference Reference, SceneReferenceState Before, SceneReferenceState After);
public sealed record SceneEditImpact(IReadOnlyList<SceneReferenceChange> References, IReadOnlyList<string> ChangedGroups, IReadOnlyList<SceneReferenceDiagnostic> Coverage, bool Complete)
{
    public int ArchivedInputs { get; init; }
    public bool RequiresReview => References.Count != 0 || ChangedGroups.Count != 0 || !Complete || ArchivedInputs != 0;
}

/// <summary>Position analysis and operation previews share one classifier. Missing targets remain author warnings.</summary>
public static class SceneReferenceAnalysis
{
    public static SceneReferenceState Resolve(SceneIdentityReference reference, SceneTargetAnalysis analysis)
    {
        var result = reference.Position is { } position ? analysis.AtTimeline(reference.Owner.GroupId, reference.Owner.EntryId, position)
            : analysis.Before(reference.Owner.GroupId, reference.Owner.EntryId);
        if (reference.Slot.Domain == SceneIdentityDomain.AudioEffect)
        {
            var effects = result.AudioEffects.Where(effect => effect.Key.InstanceId == reference.Handle &&
                JsonSerializer.Serialize(new[] { effect.Key.Scope == GalNet.Contracts.Builtins.Audio.SEffectScope.Bus ? "bus" : "track", effect.Key.TargetId }) == reference.Qualifier).ToArray();
            var audioCode = !result.Reachable ? "scene.reference.unreachable" : effects.Length == 0
                ? result.AudioComplete ? "scene.reference.missing" : "scene.reference.uncertain"
                : effects.Any(effect => effect.Presence == TargetPresence.Possible) ? "scene.reference.possible" : "scene.reference.resolved";
            return new(audioCode, result.AudioComplete, result.Reachable, []) { AudioEffects = effects };
        }
        var candidates = (reference.Slot.Domain == SceneIdentityDomain.Playback ? result.Playbacks : result.Targets).Where(target => target.Handle == reference.Handle).ToArray();
        var matching = candidates.Where(target => reference.Slot.Kinds.Contains(target.Kind) && (!reference.Slot.RequiresVariants || target.Variants is not null)).ToArray();
        var code = !result.Reachable ? "scene.reference.unreachable" : matching.Length == 0
            ? !result.Complete || candidates.Any(target => target.Kind == SceneTargetKind.Unresolved) ? "scene.reference.uncertain" : "scene.reference.missing"
            : matching.Any(target => target.Presence == TargetPresence.Possible) ? "scene.reference.possible" : "scene.reference.resolved";
        return new(code, result.Complete, result.Reachable, matching);
    }
    public static IReadOnlyList<SceneReferenceDiagnostic> Diagnose(SceneReferenceIndex index, SceneTargetAnalysis analysis) => index.Coverage.Concat(index.References.Select(reference =>
    {
        var state = Resolve(reference, analysis);
        return state.Code == "scene.reference.resolved" && state.Complete ? null : new SceneReferenceDiagnostic(state.Code == "scene.reference.resolved" ? "scene.reference.uncertain" : state.Code,
            reference.Owner, reference.JsonPointer, reference.Handle, index.Candidates(reference));
    }).OfType<SceneReferenceDiagnostic>()).ToArray();

    public static SceneEditImpact Compare(EditorGraphDocument graph, IReadOnlyDictionary<string, List<EditorEntryData>> before, IReadOnlyDictionary<string, List<EditorEntryData>> after,
        IReadOnlyDictionary<SceneAuthorAddress, SceneAuthorAddress>? originalOwners = null)
    {
        var oldIndex = new SceneReferenceIndex(before); var newIndex = new SceneReferenceIndex(after);
        var oldAnalysis = new SceneTargetAnalysis(graph, before); var newAnalysis = new SceneTargetAnalysis(graph, after);
        var changes = new List<SceneReferenceChange>(); var coverage = oldIndex.Coverage.Concat(newIndex.Coverage).Distinct().ToList();
        var complete = oldIndex.Complete && newIndex.Complete;
        var oldReferences = oldIndex.References.ToDictionary(reference => (reference.Owner, reference.JsonPointer));
        // Remaining consumers only; references deleted in the same batch are intentionally excluded.
        foreach (var reference in newIndex.References)
        {
            var original = originalOwners?.GetValueOrDefault(reference.Owner) ?? reference.Owner;
            if (!oldReferences.TryGetValue((original, reference.JsonPointer), out var old)) continue;
            var previous = Resolve(old, oldAnalysis); var next = Resolve(reference, newAnalysis);
            complete &= (!previous.Reachable || previous.Complete) && (!next.Reachable || next.Complete);
            if (Signature(previous) != Signature(next)) changes.Add(new(reference, previous, next));
        }
        var groups = new List<string>();
        foreach (var group in after.Keys)
        {
            var previous = oldAnalysis.Before(group, null); var next = newAnalysis.Before(group, null);
            // Unreachable groups are still indexed, but their execution state is not a proven effect.
            if (!previous.Reachable && !next.Reachable) continue;
            complete &= previous.Complete && next.Complete && previous.AudioComplete && next.AudioComplete;
            if (ResultSignature(previous) != ResultSignature(next)) groups.Add(group);
        }
        return new(changes.AsReadOnly(), groups.AsReadOnly(), coverage.AsReadOnly(), complete);
    }
    private static string Signature(SceneReferenceState state) => JsonSerializer.Serialize(new { state.Code, state.Complete, state.Reachable, state.AudioEffects, Targets = state.Targets.OrderBy(target => target.Handle, StringComparer.Ordinal) });
    private static string ResultSignature(SceneTargetResult result) => JsonSerializer.Serialize(new { result.Complete, result.Reachable, result.Targets, result.Playbacks, result.AudioComplete, result.AudioEffects });
}
