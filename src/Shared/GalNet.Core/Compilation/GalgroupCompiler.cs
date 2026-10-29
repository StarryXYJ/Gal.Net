using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;

namespace GalNet.Core.Compilation;

/// <summary>Compiles editor-source group documents into Runtime-only primitive documents.</summary>
public static class GalgroupCompiler
{
    /// <summary>Expands every non-primitive entry and returns a deterministic compiled document plus source mapping.</summary>
    public static CompiledGroupResult Compile(GroupDocument source, IEntryCatalog catalog)
        => CompileCore(source, catalog, null);

    /// <summary>Compiles once while collecting entry-local errors; failed analyses expose no partial output.</summary>
    public static GroupCompilationAnalysis Analyze(GroupDocument source, IEntryCatalog catalog)
    {
        var errors = new List<GroupCompilationIssue>();
        var result = CompileCore(source, catalog, errors);
        return new(errors.Count == 0 ? result : null, errors.AsReadOnly());
    }

    private static CompiledGroupResult CompileCore(GroupDocument source, IEntryCatalog catalog, List<GroupCompilationIssue>? errors)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (source.Version != GroupDocument.CurrentVersion) throw new InvalidDataException($"Unsupported .rawgalgroup version '{source.Version}'.");
        if (source.Kind != GroupDocumentKind.Raw) throw new InvalidDataException("Only raw group documents can be compiled.");

        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        var compiled = new CompiledGroupDocument { Version = CompiledGroupDocument.CurrentVersion, Kind = GroupDocumentKind.Compiled };
        var sourceMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (sourceEntry, sourceIndex) in source.Entries.Select((entry, index) => (entry, index)))
        {
            var parametersReady = false;
            try
            {
                if (string.IsNullOrWhiteSpace(sourceEntry.Id) || !stableIds.Add(sourceEntry.Id))
                    throw new InvalidDataException($"Source entry #{sourceIndex + 1} must have a unique non-empty id.");

                var entry = CreateEntry(sourceEntry, sourceIndex + 1, catalog);
                parametersReady = true;
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
                    if (entry is CompositeEntry)
                        primitive.SetArguments(PrimitiveArgumentHelper.Normalize(catalog.Get(primitive.Type).DynamicParameters, primitive.Arguments));
                    compiled.Entries.Add(SerializeEntry(generatedId, primitive));
                    emittedIds.Add(generatedId);
                }

                sourceMap.Add(sourceEntry.Id, emittedIds);
            }
            catch (Exception exception) when (errors is not null)
            {
                errors.Add(new(sourceIndex, !parametersReady, exception.Message));
            }
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
                var parameters = definition.DynamicParameters;
                var rawArguments = JsonCodec.SerializeToElement(source.Parameters);
                var (arguments, batchId) = PrimitiveArgumentHelper.NormalizeCompiledArguments(parameters, rawArguments);
                var primitive = new PrimitiveEntry(source.Type, arguments, batchId)
                {
                    Id = index,
                    Condition = source.Condition
                };
                return primitive;
            }

            var normalized = PrimitiveArgumentHelper.Normalize(definition.DynamicParameters, JsonCodec.SerializeToElement(source.Parameters));
            return catalog.Create(source.Type, index, source.Condition, normalized);
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

public sealed record GroupCompilationIssue(int SourceIndex, bool IsParameterError, string Message);
public sealed record GroupCompilationAnalysis(CompiledGroupResult? Compilation, IReadOnlyList<GroupCompilationIssue> Issues);
