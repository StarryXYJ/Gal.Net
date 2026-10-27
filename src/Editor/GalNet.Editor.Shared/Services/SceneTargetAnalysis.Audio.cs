using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed partial class SceneTargetAnalysis
{
    private void TransferAudio(State state, string group, EditorEntryData entry)
    {
        if (entry.Type is not (BuiltinEntryIds.ApplyAudioSEffect or BuiltinEntryIds.StopAudioSEffect or BuiltinEntryIds.UpdateAudioSEffect))
        {
            if (SceneAuthoringSemantics.Find(entry.Type) is null) state.UncertainAudio();
            return;
        }
        string Text(string key, string fallback = "") => entry.Parameters.TryGetValue(key, out var value)
            && value.ValueKind != JsonValueKind.Null ? value.GetString() ?? fallback : fallback;
        var scope = Text("scope", "track") switch { "track" => SEffectScope.Track, "bus" => SEffectScope.Bus, _ => throw new InvalidDataException("Invalid scope.") };
        var targetId = Text("targetId");
        if (string.IsNullOrWhiteSpace(targetId) || _audioConfiguration is not null && !_audioConfiguration.HasTarget(scope, targetId))
            throw new InvalidDataException("Unknown audio target.");
        var key = new AudioEffectKey(scope, targetId, Text("instanceId"));
        if (entry.Type == BuiltinEntryIds.ApplyAudioSEffect)
        {
            if (string.IsNullOrWhiteSpace(key.InstanceId)) throw new InvalidDataException("An audio effect needs an instance identity.");
            var type = Text("id");
            var parameters = entry.Parameters.GetValueOrDefault("parameters", JsonSerializer.SerializeToElement(new { }));
            var valid = _audioCatalog.Validate(type, scope, parameters).Count == 0;
            // Unsupported metadata still has an author identity; stop can resolve and remove it.
            state.AudioEffects[key] = new(key, valid ? TargetPresence.Definite : TargetPresence.Possible, [new(group, entry.StableId, "instanceId")], [type], valid);
        }
        else if (entry.Type == BuiltinEntryIds.StopAudioSEffect)
        {
            var all = entry.Parameters.TryGetValue("all", out var value) && value.GetBoolean();
            if (all && key.InstanceId.Length != 0) throw new InvalidDataException("Conflicting stop selection.");
            if (all)
                foreach (var existing in state.AudioEffects.Keys.Where(existing => existing.Scope == scope && existing.TargetId == targetId).ToArray()) state.AudioEffects.Remove(existing);
            else state.AudioEffects.Remove(key);
        }
        else if (entry.Parameters.TryGetValue("parameters", out var patch))
        {
            if (patch.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid effect patch.");
            // Updates cannot create an absent instance or disambiguate its algorithm.
            if (state.AudioEffects.TryGetValue(key, out var effect) && effect.Types.Count == 1 && _audioCatalog.TryGet(effect.Types[0], out var definition))
            {
                if (patch.EnumerateObject().Any(item => !definition.Parameters.Any(parameter => parameter.Name == item.Name)
                    || GalNet.Core.Parameters.ValueValidation.Validate(definition.Parameters.Single(parameter => parameter.Name == item.Name).Field.Value, item.Value) is not null))
                    state.AudioComplete = false;
            }
        }
    }

    private static void MergeAudio(Dictionary<AudioEffectKey, AudioEffectTarget> result, IReadOnlyList<Dictionary<AudioEffectKey, AudioEffectTarget>> domains)
    {
        foreach (var key in domains.SelectMany(domain => domain.Keys).Distinct())
        {
            var candidates = domains.Where(domain => domain.ContainsKey(key)).Select(domain => domain[key]).ToArray();
            result[key] = new(key, candidates.Length == domains.Count && candidates.All(effect => effect.Presence == TargetPresence.Definite) ? TargetPresence.Definite : TargetPresence.Possible,
                candidates.SelectMany(effect => effect.Sources).Distinct().OrderBy(source => source.GroupId, StringComparer.Ordinal).ThenBy(source => source.EntryId, StringComparer.Ordinal).ToArray(),
                candidates.SelectMany(effect => effect.Types).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(), candidates.All(effect => effect.MetadataComplete));
        }
    }
    private static bool EqualAudio(IReadOnlyDictionary<AudioEffectKey, AudioEffectTarget> left, IReadOnlyDictionary<AudioEffectKey, AudioEffectTarget> right) => left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var other) && pair.Value.Presence == other.Presence && pair.Value.MetadataComplete == other.MetadataComplete
            && pair.Value.Types.SequenceEqual(other.Types) && pair.Value.Sources.SequenceEqual(other.Sources));
    public static bool EquivalentAudio(SceneTargetResult left, SceneTargetResult right) => left.AudioComplete == right.AudioComplete && left.Reachable == right.Reachable
        && EqualAudio(left.AudioEffects.ToDictionary(effect => effect.Key), right.AudioEffects.ToDictionary(effect => effect.Key));
}
