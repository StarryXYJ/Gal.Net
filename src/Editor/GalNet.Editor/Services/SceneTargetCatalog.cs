using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

public sealed record SceneTargetChoice(SceneTarget Target, ResourcePickerItem Item);
public sealed record SceneTargetSnapshot(string GroupId, string EntryId, long Revision, bool Complete, IReadOnlyList<SceneTargetChoice> Choices, Func<SceneTarget, bool>? Filter = null);
public sealed record SceneFieldChoice(SceneTargetSnapshot Snapshot, SceneTargetChoice? Choice, ResourcePickerItem Item, string? EmptyLabelKey = null)
{ public override string ToString() => Item.Label; }

/// <summary>Editor adapter: stable locator choices resolve to opaque runtime handles only at commit.</summary>
public sealed class SceneTargetCatalog(ProjectSceneTargets query, IEditorDocumentService documents, IAssetManager assets, IEditorLocalizationService localization)
{
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
    public IEditorLocalizationService Text => localization;
    public SceneTargetSnapshot Query(string groupId, string entryId, Func<SceneTarget, bool>? filter = null)
    {
        var result = query.Before(groupId, entryId);
        var choices = result.Targets.Where(target => target.Kind != SceneTargetKind.Unresolved && (filter?.Invoke(target) ?? true)).Select(target =>
        {
            var group = documents.CurrentDocument.Nodes.FirstOrDefault(node => node.Id == target.Locator.GroupId);
            var entry = documents.Groups.TryGetValue(target.Locator.GroupId, out var source) ? source.Entries.FirstOrDefault(item => item.StableId == target.Locator.EntryId) : null;
            var origin = (group?.Name ?? "") + " / " + entry?.Id;
            var resource = _paths.GetValueOrDefault(target.Resource, target.Resource);
            var name = string.IsNullOrWhiteSpace(resource) || Guid.TryParse(resource, out _) ? localization["Timeline.Kind." + target.Kind] : Path.GetFileName(resource);
            var presence = localization[target.Presence == TargetPresence.Definite ? "Timeline.Definite" : "Timeline.Possible"];
            return new SceneTargetChoice(target, new(JsonSerializer.Serialize(target.Locator), name + " · " + origin,
                localization["Timeline.Kind." + target.Kind] + " · " + resource + " · " + origin + " · " + presence,
                target.Kind == SceneTargetKind.Particle ? "✦" : target.Kind == SceneTargetKind.Effect ? "◈" : "▣", Badge: presence));
        }).ToArray();
        return new(groupId, entryId, query.Revision, result.Complete, choices, filter);
    }
    public async Task PrepareAsync(string groupId, string entryId, CancellationToken token = default)
    {
        var resources = query.Before(groupId, entryId).Targets.Select(target => target.Resource).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        foreach (var resource in resources)
        {
            token.ThrowIfCancellationRequested();
            var file = await assets.GetFileAsync(resource, token);
            if (file is not null) _paths[resource] = file.Path;
        }
    }
    public bool IsCurrent(SceneTargetSnapshot snapshot)
    { query.Before(snapshot.GroupId, snapshot.EntryId); return query.IsAvailable && query.Revision == snapshot.Revision; }
    public SceneTarget? Resolve(SceneTargetSnapshot snapshot, string id)
    {
        if (!snapshot.Choices.Any(choice => choice.Item.Id == id) || !IsCurrent(snapshot)) return null;
        return Query(snapshot.GroupId, snapshot.EntryId).Choices.FirstOrDefault(choice => choice.Item.Id == id)?.Target;
    }
    public IReadOnlyList<FieldEditorOption>? FieldOptions(string groupId, string entryId, string type, string field)
    {
        var kinds = SceneFieldSemantics.TargetKinds(type, field); if (kinds is null) return null;
        var snapshot = Query(groupId, entryId, target => kinds.Contains(target.Kind));
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
        (BuiltinEntryIds.HideLayer or BuiltinEntryIds.MoveLayer or BuiltinEntryIds.ReplaceLayer or BuiltinEntryIds.StopVideo, "handleId") => [SceneTargetKind.Layer],
        (BuiltinEntryIds.Animate, "handleId") => [SceneTargetKind.Layer, SceneTargetKind.Particle, SceneTargetKind.Effect],
        (BuiltinEntryIds.ApplyEffect, "targetHandleId") => [SceneTargetKind.Layer],
        (BuiltinEntryIds.StopEffect, "instanceId") => [SceneTargetKind.Effect],
        (BuiltinEntryIds.StopParticleEmitter, "instanceId") => [SceneTargetKind.Particle],
        _ => null
    };
    public static bool IsOutput(string type, string field) => (type, field) switch
    {
        (BuiltinEntryIds.ShowLayer or BuiltinEntryIds.ShowColorLayer or BuiltinEntryIds.PlayVideo, "handleId") => true,
        (BuiltinEntryIds.PlayParticleEmitter or BuiltinEntryIds.ApplyEffect, "instanceId") => true,
        (BuiltinEntryIds.Animate, "playbackHandleId") => true,
        _ => false
    };
}
