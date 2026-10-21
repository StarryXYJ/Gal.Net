using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

public sealed record SceneTargetChoice(SceneTarget Target, ResourcePickerItem Item);
public sealed record SceneTargetSnapshot(string GroupId, string EntryId, long Revision, bool Complete, IReadOnlyList<SceneTargetChoice> Choices, Func<SceneTarget, bool>? Filter = null, SceneTimelinePosition? Position = null, bool Playback = false);
public sealed record SceneFieldChoice(SceneTargetSnapshot Snapshot, SceneTargetChoice? Choice, ResourcePickerItem Item, string? EmptyLabelKey = null)
{ public override string ToString() => Item.Label; }

/// <summary>Editor adapter: stable locator choices resolve to opaque runtime handles only at commit.</summary>
public sealed class SceneTargetCatalog(ProjectSceneTargets query, IEditorDocumentService documents, IAssetManager assets, IEditorLocalizationService localization)
{
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
    public IEditorLocalizationService Text => localization;
    public SceneTargetSnapshot Query(string groupId, string entryId, Func<SceneTarget, bool>? filter = null, SceneTimelinePosition? position = null, bool playback = false)
    {
        var result = position is null ? query.Before(groupId, entryId) : query.AtTimeline(groupId, entryId, position);
        var choices = (playback ? result.Playbacks : result.Targets).Where(target => target.Kind != SceneTargetKind.Unresolved && (filter?.Invoke(target) ?? true)).Select(target =>
        {
            var group = documents.CurrentDocument.Nodes.FirstOrDefault(node => node.Id == target.Locator.GroupId);
            var entry = documents.Groups.TryGetValue(target.Locator.GroupId, out var source) ? source.Entries.FirstOrDefault(item => item.StableId == target.Locator.EntryId) : null;
            var origin = (group?.Name ?? "") + " / " + entry?.Id;
            var resource = playback ? localization["Entry.Type." + target.Resource] : _paths.GetValueOrDefault(target.Resource, target.Resource);
            var kind = localization[playback ? "Scene.Playback" : "Timeline.Kind." + target.Kind];
            var name = playback ? resource : string.IsNullOrWhiteSpace(resource) || Guid.TryParse(resource, out _) ? kind : Path.GetFileName(resource);
            var detail = playback ? resource + " · " + target.Resource : resource;
            if (target.CreatedAtFrame is { } created && !target.IsFuture) origin += " · " + localization.Format("Scene.CreatedAt", created);
            var presence = target.IsFuture ? localization.Format("Scene.FutureAt", target.CreatedAtFrame) : localization[target.Presence == TargetPresence.Definite ? "Timeline.Definite" : "Timeline.Possible"];
            return new SceneTargetChoice(target, new(JsonSerializer.Serialize(target.Locator), name + " · " + origin,
                kind + " · " + detail + " · " + origin + " · " + presence,
                playback ? "▶" : target.Kind == SceneTargetKind.Particle ? "✦" : target.Kind == SceneTargetKind.Effect ? "◈" : "▣", Badge: presence));
        }).ToArray();
        return new(groupId, entryId, query.Revision, result.Complete, choices, filter, position, playback);
    }
    public async Task PrepareAsync(string groupId, string entryId, SceneTimelinePosition? position = null, CancellationToken token = default)
    {
        await query.PrepareAsync(token);
        var resources = (position is null ? query.Before(groupId, entryId) : query.AtTimeline(groupId, entryId, position)).Targets.Select(target => target.Resource).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        foreach (var resource in resources)
        {
            token.ThrowIfCancellationRequested();
            var file = await assets.GetFileAsync(resource, token);
            if (file is not null) _paths[resource] = file.Path;
        }
    }
    public bool IsCurrent(SceneTargetSnapshot snapshot)
    { if (snapshot.Position is null) query.Before(snapshot.GroupId, snapshot.EntryId); else query.AtTimeline(snapshot.GroupId, snapshot.EntryId, snapshot.Position); return query.IsAvailable && query.Revision == snapshot.Revision; }
    public SceneTarget? Resolve(SceneTargetSnapshot snapshot, string id)
    {
        if (!snapshot.Choices.Any(choice => choice.Item.Id == id) || !IsCurrent(snapshot)) return null;
        return Query(snapshot.GroupId, snapshot.EntryId, snapshot.Filter, snapshot.Position, snapshot.Playback).Choices.FirstOrDefault(choice => choice.Item.Id == id)?.Target;
    }
    public IReadOnlyList<FieldEditorOption>? FieldOptions(string groupId, string entryId, string type, string field, SceneTimelinePosition? position = null)
    {
        var kinds = SceneFieldSemantics.TargetKinds(type, field); if (kinds is null) return null;
        var snapshot = Query(groupId, entryId, target => kinds.Contains(target.Kind), position, type == BuiltinEntryIds.StopAnimation);
        var noneKey = type == BuiltinEntryIds.ApplyEffect ? "Scene.Global" : "Scene.None";
        var none = new ResourcePickerItem("none", localization[noneKey], "", "×");
        return new[] { new FieldEditorOption("none", new SceneFieldChoice(snapshot, null, none, noneKey), JsonSerializer.SerializeToElement("")) }
            .Concat(snapshot.Choices.Select(choice => new FieldEditorOption(choice.Item.Id, new SceneFieldChoice(snapshot, choice, choice.Item), JsonSerializer.SerializeToElement(choice.Target.Handle)))).ToArray();
    }
}

internal static class SceneFieldSemantics
{
    public static SceneTargetKind[]? TargetKinds(string type, string field) => (type, field) switch
    {
        (BuiltinEntryIds.StopAnimation, "playbackHandleId") => [SceneTargetKind.Playback],
        (BuiltinEntryIds.HideLayer or BuiltinEntryIds.MoveLayer or BuiltinEntryIds.ReplaceLayer or BuiltinEntryIds.StopVideo, "handleId") => [SceneTargetKind.Layer],
        (BuiltinEntryIds.Animate, "handleId") => [SceneTargetKind.Layer, SceneTargetKind.Particle, SceneTargetKind.Effect],
        (BuiltinEntryIds.ApplyEffect, "targetHandleId") => [SceneTargetKind.Layer],
        (BuiltinEntryIds.CrossFadeTransition or BuiltinEntryIds.BlindsTransition, "oldHandleId") => [SceneTargetKind.Layer],
        (BuiltinEntryIds.SlideTransition or BuiltinEntryIds.BlackFadeTransition or BuiltinEntryIds.WhiteFadeTransition or BuiltinEntryIds.ColorFadeTransition, "fromLayerHandleId") => [SceneTargetKind.Layer],
        (BuiltinEntryIds.StopEffect, "instanceId") => [SceneTargetKind.Effect],
        (BuiltinEntryIds.StopParticleEmitter, "instanceId") => [SceneTargetKind.Particle],
        _ => null
    };
    public static bool IsOutput(string type, string field) => SceneAuthoringIdentities.IsOutput(type, field);
    public static string? Label(string type, string field) => TargetKinds(type, field) is null ? null : type == BuiltinEntryIds.StopAnimation ? "Scene.Playback" : "Scene.Target";
}
