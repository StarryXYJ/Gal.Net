using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
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
    public static CompiledGroupResult Compile(GroupDocument source, IEntryCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (source.Version != GroupDocument.CurrentVersion) throw new InvalidDataException($"Unsupported .rawgalgroup version '{source.Version}'.");
        if (source.Kind != GroupDocumentKind.Raw) throw new InvalidDataException("Only raw group documents can be compiled.");

        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        var compiled = new CompiledGroupDocument { Version = CompiledGroupDocument.CurrentVersion, Kind = GroupDocumentKind.Compiled };
        var sourceMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (sourceEntry, sourceIndex) in source.Entries.Select((entry, index) => (entry, index)))
        {
            if (string.IsNullOrWhiteSpace(sourceEntry.Id) || !stableIds.Add(sourceEntry.Id))
                throw new InvalidDataException($"Source entry #{sourceIndex + 1} must have a unique non-empty id.");

            var entry = CreateEntry(sourceEntry, sourceIndex + 1, catalog);
            IReadOnlyList<PrimitiveEntry> primitives = entry switch
            {
                PrimitiveEntry primitive => [primitive],
                CompositeEntry composite => composite.Compile(new EntryCompileContext
                {
                    SourceEntryId = sourceEntry.Id,
                    Condition = sourceEntry.Condition
                }),
                _ => throw new InvalidDataException($"Entry '{sourceEntry.Type}' is neither a primitive nor a composite entry.")
            };

            var emittedIds = new List<string>(primitives.Count);
            var readingIndices = primitives.Select((primitive, index) => (primitive, index))
                .Where(item => catalog.Get(item.primitive.Type).IsReading).Select(item => item.index).ToArray();
            if (readingIndices.Length > 1)
                throw new InvalidDataException($"Composite '{sourceEntry.Id}' contains multiple reading boundaries. Split it into separate source entries.");
            var readingIndex = readingIndices.FirstOrDefault(-1);
            if (readingIndex >= 0)
            {
                if (primitives.Take(readingIndex + 1).Any(primitive => primitive.Condition != primitives[readingIndex].Condition))
                    throw new InvalidDataException($"Composite '{sourceEntry.Id}' has inconsistent reading-boundary conditions.");
                primitives[0].ReadingBoundaryId = $"{sourceEntry.Id}#{readingIndex + 1}";
            }
            for (var emittedIndex = 0; emittedIndex < primitives.Count; emittedIndex++)
            {
                var primitive = primitives[emittedIndex];
                primitive.Id = compiled.Entries.Count + 1;
                primitive.Condition = CombineConditions(sourceEntry.Condition, primitive.Condition);
                var generatedId = $"{sourceEntry.Id}#{emittedIndex + 1}";
                var schema = catalog.Get(primitive.Type);
                primitive.SetArguments(PrimitiveArgumentHelper.Normalize(schema.DynamicParameters!, primitive.Arguments));
                compiled.Entries.Add(SerializeEntry(generatedId, primitive));
                emittedIds.Add(generatedId);
            }

            sourceMap.Add(sourceEntry.Id, emittedIds);
        }

        return new CompiledGroupResult(compiled, sourceMap);
    }


    private static Entry.Entry CreateEntry(GroupEntryDocument source, int index, IEntryCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(source.Type)) throw new InvalidDataException("Source entry type is required.");

        try
        {
            var definition = catalog.Get(source.Type);
            if (definition.Kind == EntryKind.Primitive)
            {
                var parameters = definition.DynamicParameters ?? throw new InvalidDataException($"Primitive '{source.Type}' has no dynamic parameter schema.");
                var rawArguments = JsonSerializer.SerializeToElement(source.Parameters);
                var (arguments, batchId) = PrimitiveArgumentHelper.NormalizeCompiledArguments(parameters, rawArguments);
                var primitive = new PrimitiveEntry(source.Type, arguments, batchId)
                {
                    Id = index,
                    Condition = source.Condition
                };
                return primitive;
            }

            return catalog.Create(source.Type, index, source.Condition, JsonSerializer.SerializeToElement(source.Parameters));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
        {
            throw new InvalidDataException($"Invalid source entry '{source.Id}' ({source.Type}): {exception.Message}", exception);
        }
    }

    private static PrimitiveEntryDocument SerializeEntry(string generatedId, PrimitiveEntry entry)
    {
        return new PrimitiveEntryDocument
        {
            Id = generatedId,
            TypeId = entry.Type,
            BatchId = entry.BatchId,
            ReadingBoundaryId = entry.ReadingBoundaryId,
            Condition = entry.Condition,
            Arguments = entry.Arguments.Clone()
        };
    }

    private static string CombineConditions(string parent, string child)
    {
        if (string.IsNullOrWhiteSpace(parent)) return child;
        if (string.IsNullOrWhiteSpace(child) || string.Equals(parent, child, StringComparison.Ordinal)) return parent;
        return $"({parent}) && ({child})";
    }
}

/// <summary>Compiled group plus stable source-to-generated entry mapping for diagnostics and editor navigation.</summary>
public sealed record CompiledGroupResult(
    CompiledGroupDocument Document,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SourceMap);
