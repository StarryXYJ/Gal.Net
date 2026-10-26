using System.Text.Json;
using GalNet.Core.Primitives;

namespace GalNet.Core.Entry;

/// <summary>Common instruction identity and owned JSON arguments.</summary>
public abstract class Entry
{
    public int Id { get; set; }
    public string Condition { get; set; } = "";
    public abstract string Type { get; }
    public JsonElement Arguments { get; private set; } = JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    public void SetArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Entry arguments must be a JSON object.", nameof(arguments));
        Arguments = arguments.Clone();
    }
}

/// <summary>A compiled invocation; authoring entries are distinct types.</summary>
public sealed class PrimitiveEntry : Entry
{
    public PrimitiveEntry(string typeId, JsonElement arguments, string? batchId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        Type = typeId;
        SetArguments(arguments);
        BatchId = NormalizeBatchId(batchId);
    }
    public override string Type { get; }
    public string? BatchId { get; set; }
    public string StableId { get; set; } = "";
    public string? ReadingBoundaryId { get; set; }
    internal static string? NormalizeBatchId(string? batchId) => string.IsNullOrWhiteSpace(batchId) ? null : batchId;
}

/// <summary>A draft primitive containing author JSON; compilation creates a PrimitiveEntry.</summary>
public sealed class AuthoringPrimitiveEntry(string typeId) : Entry
{
    public override string Type { get; } = !string.IsNullOrWhiteSpace(typeId) ? typeId
        : throw new ArgumentException("Primitive type ID is required.", nameof(typeId));
}

public abstract class CompositeEntry : Entry
{
    public abstract IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context);
}

public sealed class EntryCompileContext
{
    public required string SourceEntryId { get; init; }
    public required string Condition { get; init; }
}
/// <summary>Schema used to create, validate, and edit one registered entry type.</summary>
/// <remarks>Parameter defaults and constraints are owned by the immutable parameter table.</remarks>
public sealed class EntryDefinition
{
    public EntryDefinition(string type, string category, Func<Entry> factory, EntryKind kind, DynamicParameterTable parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(parameters);
        Type = type;
        Category = category;
        Factory = factory;
        Kind = kind;
        DynamicParameters = parameters;
    }
    public string Type { get; }
    public string Category { get; }
    public Func<Entry> Factory { get; }
    public EntryKind Kind { get; }
    public DynamicParameterTable DynamicParameters { get; }
    public bool IsReading { get; init; }
    public Type? FieldDataType { get; init; }
}

/// <summary>
/// Composition-supplied authoring catalog. It joins primitive descriptors from a
/// host/module set with Core's non-primitive authoring expansions.
/// </summary>
public interface IEntryCatalog
{
    IReadOnlyList<EntryDefinition> Definitions { get; }
    bool TryGet(string type, out EntryDefinition definition);
    EntryDefinition Get(string type);
    Entry Create(string type, int id = 0, string condition = "", JsonElement? arguments = null);
}

/// <summary>Whether an entry is directly executable or requires compilation first.</summary>
public enum EntryKind
{
    Primitive,
    Composite
}
