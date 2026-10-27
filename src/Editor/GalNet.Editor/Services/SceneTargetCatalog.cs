using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Variants;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

public sealed record SceneTargetChoice(SceneTarget Target, ResourcePickerItem Item);
public sealed record SceneTargetSnapshot(string GroupId, string EntryId, long Revision, bool Complete, IReadOnlyList<SceneTargetChoice> Choices, Func<SceneTarget, bool>? Filter = null, SceneTimelinePosition? Position = null, bool Playback = false, long ContextRevision = 0);
public sealed record SceneFieldChoice(SceneTargetSnapshot Snapshot, SceneTargetChoice? Choice, ResourcePickerItem Item, string? EmptyLabelKey = null)
{ public override string ToString() => Item.Label; }
public sealed record VariantFieldSource(SceneTargetSnapshot Snapshot, ImageVariantsDefinition? Definition, string? Dimension);

/// <summary>Editor adapter: stable locator choices resolve to opaque runtime handles only at commit.</summary>
public sealed partial class SceneTargetCatalog : IDisposable
{
    private readonly ProjectSceneTargets query;
    private readonly IEditorDocumentService documents;
    private readonly IEditorLocalizationService localization;
    private readonly SceneSourceDescriptions _descriptions;
    private long _resourceEpoch;
    public SceneTargetCatalog(ProjectSceneTargets query, IEditorDocumentService documents, IAssetManager assets, IEditorLocalizationService localization)
    {
        this.query = query; this.documents = documents; this.localization = localization;
        _descriptions = new(documents, assets, localization); _resourceEpoch = query.ResourceEpoch;
        query.Changed += OnQueryChanged;
    }
    private void OnQueryChanged() { if (_resourceEpoch != query.ResourceEpoch) { _resourceEpoch = query.ResourceEpoch; _descriptions.Invalidate(); } }
    public IEditorLocalizationService Text => localization;
    public event Action? Changed { add { query.Changed += value; _descriptions.Changed += value; } remove { query.Changed -= value; _descriptions.Changed -= value; } }
    internal string ResourceLabel(string id) => _descriptions.Resource(id);
    internal (long Author, long Resources) PreparationStamp(SceneTargetSnapshot snapshot) => query.PreparationStamp(snapshot.GroupId, snapshot.EntryId, snapshot.Position);
    internal bool IsSamePreparation(SceneTargetSnapshot snapshot, (long Author, long Resources) stamp)
    { var current = PreparationStamp(snapshot); return query.IsAvailable && current == stamp; }
    public VariantFieldSource? VariantsSource(IEntryFieldOrigin? origin)
    {
        if (origin?.GroupId is not { } groupId || !documents.Groups.TryGetValue(groupId, out var group)
            || group.Entries.FirstOrDefault(entry => entry.StableId == origin.EntryId) is not { } entry) return null;
        var snapshot = Query(groupId, entry.StableId);
        string Read(string key) => entry.Parameters.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var id = entry.Type == BuiltinEntryIds.ShowVariants ? Read("assetId") : snapshot.Choices.FirstOrDefault(choice => choice.Target.Handle == Read("handleId"))?.Target.Variants is { Definite: true, AssetIds.Count: 1 } binding ? binding.AssetIds[0] : "";
        return new(snapshot, id.Length == 0 ? null : query.VariantDefinition(id), Read("dimension"));
    }
    public SceneTargetSnapshot Query(string groupId, string entryId, Func<SceneTarget, bool>? filter = null, SceneTimelinePosition? position = null, bool playback = false)
    {
        var result = position is null ? query.Before(groupId, entryId) : query.AtTimeline(groupId, entryId, position);
        var choices = (playback ? result.Playbacks : result.Targets).Where(target => target.Kind != SceneTargetKind.Unresolved && (filter?.Invoke(target) ?? true)).Select(target => Describe(target, playback)).ToArray();
        return new(groupId, entryId, result.Revision, result.Complete, choices, filter, position, playback, result.ContextRevision);
    }
    internal string ReferenceLabel(string group, string entry, SceneReferenceSlot slot, string raw)
    {
        if (slot.Domain == SceneIdentityDomain.AudioEffect)
        {
            var snapshot = QueryAudio(group, entry, retain: false);
            var effect = snapshot.Targets.FirstOrDefault(effect => effect.Key.InstanceId == raw);
            return effect is null ? localization["Scene.Unresolved"] : AudioDescription(effect);
        }
        var result = query.InspectBefore(group, entry);
        var playback = slot.Domain == SceneIdentityDomain.Playback;
        var target = (playback ? result.Playbacks : result.Targets).FirstOrDefault(target => target.Handle == raw && slot.Kinds.Contains(target.Kind)
            && (!slot.RequiresVariants || target.Variants is not null));
        return target is null ? localization["Scene.Unresolved"] : Describe(target, playback).Item.Label;
    }
    private SceneTargetChoice Describe(SceneTarget target, bool playback)
    {
        var origin = _descriptions.Origin(target);
        var resourceId = string.IsNullOrWhiteSpace(target.Resource) && target.Variants is { AssetIds.Count: 1 } variants ? variants.AssetIds[0] : target.Resource;
        var resource = playback ? localization["Entry.Type." + target.Resource] : _descriptions.Resource(resourceId);
        var kind = localization[playback ? "Scene.Playback" : "Timeline.Kind." + target.Kind];
        var name = playback ? resource : string.IsNullOrWhiteSpace(resource) || Guid.TryParse(resource, out _) ? kind : Path.GetFileName(resource);
        var detail = playback ? resource + " · " + target.Resource : resource;
        if (target.Variants is { Definite: false }) detail += " · " + localization["Variants.AmbiguousSource"];
        if (target.CreatedAtFrame is { } created && !target.IsFuture) origin += " · " + localization.Format("Scene.CreatedAt", created);
        var presence = target.IsFuture ? localization.Format("Scene.FutureAt", target.CreatedAtFrame) : localization[target.Presence == TargetPresence.Definite ? "Timeline.Definite" : "Timeline.Possible"];
        return new SceneTargetChoice(target, new(JsonSerializer.Serialize(target.Locator), name + " · " + origin,
            kind + " · " + detail + " · " + origin + " · " + presence,
            playback ? "▶" : target.Kind == SceneTargetKind.Particle ? "✦" : target.Kind == SceneTargetKind.Effect ? "◈" : "▣", Badge: presence));
    }
    public async Task PrepareAsync(string groupId, string entryId, SceneTimelinePosition? position = null, CancellationToken token = default)
    {
        await query.PrepareAsync(token);
        var resources = (position is null ? query.Before(groupId, entryId) : query.AtTimeline(groupId, entryId, position)).Targets
            .SelectMany(target => new[] { target.Resource }.Concat(target.Variants?.AssetIds ?? [])).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        foreach (var resource in resources)
        {
            token.ThrowIfCancellationRequested();
            await _descriptions.PrepareAsync(resource).WaitAsync(token);
        }
    }
    public bool IsCurrent(SceneTargetSnapshot snapshot)
    { var current = snapshot.Position is null ? query.Before(snapshot.GroupId, snapshot.EntryId) : query.AtTimeline(snapshot.GroupId, snapshot.EntryId, snapshot.Position); return query.IsAvailable && current.Revision == snapshot.Revision && current.ContextRevision == snapshot.ContextRevision; }
    public SceneTarget? Resolve(SceneTargetSnapshot snapshot, string id)
    {
        if (!snapshot.Choices.Any(choice => choice.Item.Id == id) || !IsCurrent(snapshot)) return null;
        return Query(snapshot.GroupId, snapshot.EntryId, snapshot.Filter, snapshot.Position, snapshot.Playback).Choices.FirstOrDefault(choice => choice.Item.Id == id)?.Target;
    }
    public IReadOnlyList<FieldEditorOption>? FieldOptions(string groupId, string entryId, string type, string field, SceneTimelinePosition? position = null)
    {
        if (type == BuiltinEntryIds.Animate && field == "property")
        {
            if (!documents.Groups.TryGetValue(groupId, out var group) || group.Entries.FirstOrDefault(entry => entry.StableId == entryId) is not { } entry
                || !entry.Parameters.TryGetValue("handleId", out var handleValue) || handleValue.ValueKind != JsonValueKind.String)
                return [];
            var handle = handleValue.GetString();
            var target = Query(groupId, entryId, position: position).Choices.FirstOrDefault(choice => choice.Target.Handle == handle)?.Target;
            return target?.Properties.Select(property =>
            {
                var key = "Timeline.Property." + property.Name;
                var label = localization[key];
                return new FieldEditorOption(property.Name, label == "[" + key + "]" ? property.Name : label, JsonSerializer.SerializeToElement(property.Name));
            }).ToArray() ?? [];
        }
        if (SceneAuthoringSemantics.Reference(type, field)?.Domain == SceneIdentityDomain.AudioEffect) return AudioFieldOptions(groupId, entryId, type);
        var kinds = SceneFieldSemantics.TargetKinds(type, field); if (kinds is null) return null;
        var snapshot = Query(groupId, entryId, target => kinds.Contains(target.Kind) && (SceneAuthoringSemantics.Reference(type, field)?.RequiresVariants != true || target.Variants is not null), position, SceneAuthoringSemantics.Reference(type, field)?.Domain == SceneIdentityDomain.Playback);
        var noneKey = SceneAuthoringSemantics.Reference(type, field)?.AllowsGlobal == true ? "Scene.Global" : "Scene.None";
        var none = new ResourcePickerItem("none", localization[noneKey], "", "×");
        return new[] { new FieldEditorOption("none", new SceneFieldChoice(snapshot, null, none, noneKey), JsonSerializer.SerializeToElement("")) }
            .Concat(snapshot.Choices.Select(choice => new FieldEditorOption(choice.Item.Id, new SceneFieldChoice(snapshot, choice, choice.Item), JsonSerializer.SerializeToElement(choice.Target.Handle)))).ToArray();
    }
    public void Dispose() { query.Changed -= OnQueryChanged; _descriptions.Dispose(); }
}

internal static class SceneFieldSemantics
{
    public static IReadOnlyList<SceneTargetKind>? TargetKinds(string type, string field) => SceneAuthoringSemantics.Reference(type, field)?.Kinds;
    public static bool IsOutput(string type, string field) => SceneAuthoringIdentities.IsOutput(type, field) || type == GalNet.Contracts.Builtins.BuiltinEntryIds.StopAudioSEffect && field == "all";
    public static string? Label(string type, string field) => type is GalNet.Contracts.Builtins.BuiltinEntryIds.ApplyAudioSEffect or GalNet.Contracts.Builtins.BuiltinEntryIds.StopAudioSEffect or GalNet.Contracts.Builtins.BuiltinEntryIds.UpdateAudioSEffect
        ? field switch { "id" => "Audio.EffectType", "targetId" => "Audio.Target", "parameters" => "Audio.Parameters", "instanceId" => "Audio.Effect", _ => null }
        : TargetKinds(type, field) is null ? null : SceneAuthoringSemantics.Reference(type, field)?.Domain switch
        { SceneIdentityDomain.Playback => "Scene.Playback", _ => "Scene.Target" };
}
