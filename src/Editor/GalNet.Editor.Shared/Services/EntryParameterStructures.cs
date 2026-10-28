using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

/// <summary>Metadata captured for the same author/resource generation as an analysis.</summary>
public sealed record EntryParameterMetadata(IEffectCatalog? Effects = null, ISEffectCatalog? AudioEffects = null,
    IReadOnlyDictionary<string, ShaderEffectDescriptor?>? Programs = null, long? Revision = null)
{
    public static IEnumerable<string> ProgramIds(EditorProjectDocument document) => document.GroupEntries.Values.SelectMany(entries => entries)
        .SelectMany(ReferencedPrograms).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal);
    private static IEnumerable<string> ReferencedPrograms(EditorEntryData entry)
    {
        if (entry.Type is BuiltinEntryIds.ApplyEffect or BuiltinEntryIds.SpeedLinesPreset or BuiltinEntryIds.ImpactPreset)
            yield return EntryParameterStructures.Text(entry, "program");
        if (entry.Type != BuiltinEntryIds.PlayTimeline || entry.Parameters.GetValueOrDefault("timeline") is not { ValueKind: JsonValueKind.Object } timeline) yield break;
        foreach (var events in timeline.EnumerateObject().Where(property => property.Name.Equals("events", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Array))
            foreach (var item in events.Value.EnumerateArray().Take(10_000))
                if (EntryParameterStructures.EventEntry(item) is { Type: BuiltinEntryIds.ApplyEffect } effect) yield return EntryParameterStructures.Text(effect, "program");
    }
}

/// <summary>Captures parameter declarations and their resource revision as one analysis snapshot.</summary>
public interface IEntryParameterMetadataSource
{
    long Revision { get; }
    EntryParameterMetadata Capture(EditorProjectDocument document);
}

internal readonly record struct EntryStructureContext(string GroupId, EditorEntryData Entry);

/// <summary>Resolves domain JSON fields before handing them to the shared property walker.</summary>
internal sealed class EntryParameterStructures(EditorProjectDocument document, EntryParameterMetadata metadata, IEntryCatalog catalog) : IValueStructureProvider
{
    private readonly ISEffectCatalog _audio = metadata.AudioEffects ?? SEffectCatalog.Builtins;
    private static readonly FieldDescriptor ShaderExtension = new("extension", new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Scalar()));
    private SceneTargetAnalysis? _analysis;

    public FieldDescriptor Describe(FieldDescriptor field, string group, EditorEntryData entry)
    {
        // Explicit contract structures, including custom providers, take precedence.
        if (field.Value.ValueType != typeof(JsonElement) || field.Value.Structure is not null) return field;
        if (field.Id == "timeline" && entry.Type == BuiltinEntryIds.PlayTimeline) return TimelineValueStructure.Describe(field);
        if (field.Id != "parameters") return field;
        var structure = ParameterStructure(group, entry);
        return structure is null ? field : new(field.Id, field.Value.WithStructure(structure), field.IsRequired,
            field.DefaultValue, field.Editor, field.LabelKey, field.DescriptionKey, field.Semantic, field.ResourceTypeId);
    }

    private ValueStructure? ParameterStructure(string group, EditorEntryData entry)
    {
        if (entry.Type is BuiltinEntryIds.ApplyEffect or BuiltinEntryIds.SpeedLinesPreset or BuiltinEntryIds.ImpactPreset)
        {
            var program = Text(entry, "program");
            if (!string.IsNullOrWhiteSpace(program))
                return metadata.Programs?.GetValueOrDefault(program) is { } shader
                    ? ValueStructure.OpenObject(shader.Parameters.Select(ShaderEffectFields.Create), ShaderExtension) : null;
            return entry.Type == BuiltinEntryIds.ApplyEffect && metadata.Effects?.TryGet(Text(entry, "id"), out var effect) == true
                ? ValueStructure.Object(effect.Parameters.Select(parameter => parameter.Field)) : null;
        }
        if (entry.Type is BuiltinEntryIds.ApplyAudioSEffect or BuiltinEntryIds.UpdateAudioSEffect)
        {
            var type = entry.Type == BuiltinEntryIds.ApplyAudioSEffect ? Text(entry, "id") : AudioType(group, entry);
            return type is not null && _audio.TryGet(type, out var effect)
                ? ValueStructure.Object(effect.Parameters.Select(parameter => parameter.Field)) : null;
        }
        return null;
    }

    public ValueStructure? Resolve(string id, FieldDescriptor field, JsonElement? value, PropertyPath path, object? context)
    {
        // Event paths are relative to the timeline root: events/index/parameters.
        if (id != TimelineValueStructure.EventParameters || path.Count != 3 || path[1].Kind != PropertySegmentKind.Index
            || context is not EntryStructureContext owner) return null;
        if (owner.Entry.Parameters.GetValueOrDefault("timeline") is not { ValueKind: JsonValueKind.Object } timeline
            || !timeline.TryGetProperty(path[0].Value, out var events) || events.ValueKind != JsonValueKind.Array
            || !int.TryParse(path[1].Value, out var index) || index >= events.GetArrayLength()
            || EventEntry(events[index]) is not { } entry || !TimelineEventCapabilities.Supports(entry.Type)
            || !catalog.TryGet(entry.Type, out var definition)) return null;
        return ValueStructure.Object(definition.DynamicParameters.Fields.Select(child => Describe(child, owner.GroupId, entry)));
    }

    internal static EditorEntryData? EventEntry(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var types = value.EnumerateObject().Where(property => property.Name.Equals("type", StringComparison.OrdinalIgnoreCase)).ToArray();
        var parameters = value.EnumerateObject().Where(property => property.Name.Equals("parameters", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (types.Length != 1 || types[0].Value.ValueKind != JsonValueKind.String || parameters.Length != 1 || parameters[0].Value.ValueKind != JsonValueKind.Object) return null;
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in parameters[0].Value.EnumerateObject()) if (!values.TryAdd(item.Name, item.Value)) return null;
        return new() { Type = types[0].Value.GetString()!, Parameters = values };
    }

    private string? AudioType(string group, EditorEntryData entry)
    {
        var scope = Text(entry, "scope") switch { "" or "track" => SEffectScope.Track, "bus" => SEffectScope.Bus, _ => (SEffectScope?)null };
        if (scope is null) return null;
        _analysis ??= new(document.Graph, document.GroupEntries, audioCatalog: _audio);
        var before = _analysis.Before(group, entry.StableId);
        if (!before.Reachable || !before.AudioComplete) return null;
        var key = new AudioEffectKey(scope.Value, Text(entry, "targetId"), Text(entry, "instanceId"));
        var target = before.AudioEffects.FirstOrDefault(effect => effect.Key == key);
        return target is { Types.Count: 1, MetadataComplete: true } ? target.Types[0] : null;
    }

    internal static string Text(EditorEntryData entry, string name) => entry.Parameters.GetValueOrDefault(name) is { ValueKind: JsonValueKind.String } text ? text.GetString() ?? "" : "";
}
