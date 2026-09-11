using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Scene;
using GalNet.Core.Serialization;

namespace GalNet.Core.Compilation;

/// <summary>Compiles editor-source group documents into Runtime-only primitive documents.</summary>
public static class GalgroupCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Expands every non-primitive entry and returns a deterministic compiled document plus source mapping.</summary>
    public static CompiledGroupDocument Compile(GroupDocument source)
    {
        if (source.Version != 1) throw new InvalidDataException($"Unsupported .rawgalgroup version '{source.Version}'.");
        if (source.Kind != GroupDocumentKind.Raw) throw new InvalidDataException("Only raw group documents can be compiled.");

        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        var compiled = new GroupDocument { Version = source.Version, Kind = GroupDocumentKind.Compiled };
        var sourceMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (sourceEntry, sourceIndex) in source.Entries.Select((entry, index) => (entry, index)))
        {
            if (string.IsNullOrWhiteSpace(sourceEntry.Id) || !stableIds.Add(sourceEntry.Id))
                throw new InvalidDataException($"Source entry #{sourceIndex + 1} must have a unique non-empty id.");

            var entry = CreateEntry(sourceEntry, sourceIndex + 1);
            IReadOnlyList<PrimitiveEntry> primitives = entry switch
            {
                PrimitiveEntry primitive => [primitive],
                NonPrimitiveEntry nonPrimitive => nonPrimitive.Compile(new EntryCompileContext
                {
                    SourceEntryId = sourceEntry.Id,
                    Condition = sourceEntry.Condition
                }),
                _ => throw new InvalidDataException($"Entry '{sourceEntry.Type}' is neither a primitive nor a non-primitive entry.")
            };

            var emittedIds = new List<string>(primitives.Count);
            for (var emittedIndex = 0; emittedIndex < primitives.Count; emittedIndex++)
            {
                var primitive = primitives[emittedIndex];
                var definition = EntryRegistry.Get(primitive.Type);
                if (definition.Kind != EntryKind.Primitive)
                    throw new InvalidDataException($"Entry '{sourceEntry.Id}' emitted non-primitive '{primitive.Type}'.");

                primitive.Id = compiled.Entries.Count + 1;
                primitive.Condition = CombineConditions(sourceEntry.Condition, primitive.Condition);
                ValidateNestedPrimitives(primitive);

                var generatedId = $"{sourceEntry.Id}#{emittedIndex + 1}";
                compiled.Entries.Add(SerializeEntry(generatedId, primitive));
                emittedIds.Add(generatedId);
            }

            sourceMap.Add(sourceEntry.Id, emittedIds);
        }

        return new CompiledGroupDocument(compiled, sourceMap);
    }
    
    
    private static Entry.Entry CreateEntry(GroupEntryDocument source, int index)
    {
        if (string.IsNullOrWhiteSpace(source.Type)) throw new InvalidDataException("Source entry type is required.");
        try
        {
            return EntryRegistry.Create(
                source.Type,
                index,
                source.Condition,
                source.Parameters.ToDictionary(pair => pair.Key, pair => ToValue(pair.Value), StringComparer.Ordinal));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
        {
            throw new InvalidDataException($"Invalid source entry '{source.Id}' ({source.Type}): {exception.Message}", exception);
        }
    }

    private static GroupEntryDocument SerializeEntry(string generatedId, PrimitiveEntry entry)
    {
        var definition = EntryRegistry.Get(entry.Type);
        return new GroupEntryDocument
        {
            Id = generatedId,
            Type = entry.Type,
            Condition = entry.Condition,
            Parameters = entry.Values.ToDictionary(
                pair => pair.Key,
                pair => ToJsonElement(pair.Value, definition.Parameters[pair.Key]),
                StringComparer.Ordinal)
        };
    }

    private static void ValidateNestedPrimitives(PrimitiveEntry entry)
    {
        if (entry is not PlayAnimationPlanEntry || !entry.Values.TryGetValue("plan", out var planJson)) return;

        AnimationPlanDefinition plan;
        try
        {
            plan = JsonSerializer.Deserialize<AnimationPlanDefinition>(planJson, JsonOptions)
                ?? throw new InvalidDataException("Animation plan is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Animation plan is invalid JSON.", exception);
        }

        foreach (var timelineEvent in plan.Events)
        {
            var definition = EntryRegistry.Get(timelineEvent.Type);
            if (definition.Kind != EntryKind.Primitive)
                throw new InvalidDataException($"Animation plan event '{timelineEvent.Type}' must be a primitive entry.");
        }
    }

    private static JsonElement ToJsonElement(string value, EntryParameterType type)
    {
        if (type != EntryParameterType.Json) return JsonSerializer.SerializeToElement(value);

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Expected JSON parameter value, but received '{value}'.", exception);
        }
    }

    private static string ToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
        JsonValueKind.Null => "",
        _ => throw new InvalidDataException("Entry parameter values cannot be undefined.")
    };

    private static string CombineConditions(string parent, string child)
    {
        if (string.IsNullOrWhiteSpace(parent)) return child;
        if (string.IsNullOrWhiteSpace(child) || string.Equals(parent, child, StringComparison.Ordinal)) return parent;
        return $"({parent}) && ({child})";
    }
}

/// <summary>Compiled group plus stable source-to-generated entry mapping for diagnostics and editor navigation.</summary>
public sealed record CompiledGroupDocument(
    GroupDocument Document,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SourceMap);
