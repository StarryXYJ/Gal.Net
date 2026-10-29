using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

public sealed record AudioEffectSnapshot(string GroupId, string EntryId, SEffectScope Scope, string TargetId,
    long Revision, long ContextRevision, bool Complete, IReadOnlyList<AudioEffectTarget> Targets);
public sealed record AudioFieldChoice(AudioEffectSnapshot Snapshot, AudioEffectTarget? Target, string Label, bool All = false)
{ public override string ToString() => Label; }

public sealed partial class SceneTargetCatalog
{
    public ISEffectCatalog AudioCatalog => query.AudioCatalog;
    public string AudioTypeName(string type) => localization["Audio.Type." + type] is { } label && label != "Audio.Type." + type ? label : type;
    public AudioEffectSnapshot QueryAudio(string groupId, string entryId, bool retain = true)
    {
        var entry = documents.Groups.GetValueOrDefault(groupId)?.Entries.FirstOrDefault(entry => entry.StableId == entryId);
        var scope = entry?.Parameters.GetValueOrDefault("scope") is { ValueKind: JsonValueKind.String } value && value.GetString() == "bus" ? SEffectScope.Bus : SEffectScope.Track;
        var targetId = entry?.Parameters.GetValueOrDefault("targetId") is { ValueKind: JsonValueKind.String } target ? target.GetString() ?? "" : "";
        var result = retain ? query.Before(groupId, entryId) : query.InspectBefore(groupId, entryId);
        return new(groupId, entryId, scope, targetId, result.AudioRevision, result.ContextRevision, result.Reachable && result.AudioComplete,
            result.AudioEffects.Where(effect => effect.Key.Scope == scope && effect.Key.TargetId == targetId).ToArray());
    }
    public bool IsCurrent(AudioEffectSnapshot snapshot)
    {
        if (!query.IsAvailable) return false;
        var current = QueryAudio(snapshot.GroupId, snapshot.EntryId);
        return current.Revision == snapshot.Revision && current.ContextRevision == snapshot.ContextRevision && current.Scope == snapshot.Scope && current.TargetId == snapshot.TargetId;
    }
    private IReadOnlyList<FieldEditorOption> AudioFieldOptions(string groupId, string entryId, string type)
    {
        var snapshot = QueryAudio(groupId, entryId);
        var options = snapshot.Targets.Select(target => new FieldEditorOption(JsonSerializer.Serialize(target.Key),
            new AudioFieldChoice(snapshot, target, AudioDescription(target)
                + " · " + localization[target.Presence == TargetPresence.Definite ? "Timeline.Definite" : "Timeline.Possible"]),
            JsonSerializer.SerializeToElement(target.Key.InstanceId))).ToList();
        if (type == BuiltinEntryIds.StopAudioSEffect)
            options.Insert(0, new("all", new AudioFieldChoice(snapshot, null, localization["Audio.AllEffects"], All: true), JsonSerializer.SerializeToElement<object?>(null)));
        return options;
    }
    private string AudioDescription(AudioEffectTarget target) => string.Join(" / ", target.Types.Select(AudioTypeName)) + " · " + string.Join("; ", target.Sources.Select(_descriptions.Origin));
}
