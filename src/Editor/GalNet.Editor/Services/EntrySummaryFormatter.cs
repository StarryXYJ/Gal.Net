using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

internal static class EntrySummaryFormatter
{
    public static string Build(EditorEntryData entry, Func<string, string?>? text = null, SceneTargetCatalog? scenes = null, string? group = null)
    {
        List<string> parts = [];
        if (entry.Type is GalNet.Contracts.Builtins.BuiltinEntryIds.ApplyAudioSEffect or GalNet.Contracts.Builtins.BuiltinEntryIds.StopAudioSEffect or GalNet.Contracts.Builtins.BuiltinEntryIds.UpdateAudioSEffect)
        {
            if (entry.Parameters.GetValueOrDefault("targetId") is { ValueKind: JsonValueKind.String } target) parts.Add(target.GetString() ?? "");
            if (entry.Type == GalNet.Contracts.Builtins.BuiltinEntryIds.ApplyAudioSEffect && entry.Parameters.GetValueOrDefault("id") is { ValueKind: JsonValueKind.String } effectType)
                parts.Add(scenes?.AudioTypeName(effectType.GetString() ?? "") ?? effectType.GetString() ?? "");
            if (entry.Type == GalNet.Contracts.Builtins.BuiltinEntryIds.StopAudioSEffect && entry.Parameters.GetValueOrDefault("all").ValueKind == JsonValueKind.True)
                parts.Add(scenes?.Text["Audio.AllEffects"] ?? "All effects");
        }
        foreach (var (field, value) in entry.Parameters)
        {
            if (field is not ("content" or "speaker" or "asset" or "assetId" or "expression" or "toAssetId" or "program")) continue;
            if (SceneAuthoringSemantics.Reference(entry.Type, field) is not null || SceneAuthoringIdentities.IsOutput(entry.Type, field)) continue;
            var raw = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
            if (field == "content" && text is not null) raw = text(raw) ?? raw;
            else if (field is "asset" or "assetId" or "toAssetId" or "program") raw = scenes?.ResourceLabel(raw) ?? (Guid.TryParse(raw, out _) ? "" : raw);
            if (!string.IsNullOrWhiteSpace(raw)) parts.Add(raw);
        }
        if (scenes is not null && group is not null)
            foreach (var slot in SceneAuthoringSemantics.Find(entry.Type)?.References ?? [])
            {
                if (!entry.Parameters.TryGetValue(slot.Field, out var value) || value.ValueKind != JsonValueKind.String) continue;
                var raw = value.GetString();
                if (string.IsNullOrWhiteSpace(raw)) { if (slot.AllowsGlobal) parts.Add(scenes.Text["Scene.Global"]); continue; }
                parts.Add(scenes.ReferenceLabel(group, entry.StableId, slot, raw!));
            }
        return string.Join(" · ", parts.Distinct(StringComparer.Ordinal)).Replace('\n', ' ');
    }
}
